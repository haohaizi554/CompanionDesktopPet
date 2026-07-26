namespace CompanionDesktopPet.Services;

public sealed class CompanionEventPump
{
    public static TimeSpan DefaultIdleReturnThreshold { get; } = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private readonly TimeSpan _idleReturnThreshold;
    private readonly HashSet<CompanionEvent> _pending = [];
    private DateTime _lastObservedAt;
    private TimeSpan? _lastObservedIdle;
    private DateTime? _lastEmittedStoryDueAt;
    private DateTime? _pendingStoryDueAt;

    public CompanionEventPump(
        DateTime initialNow,
        TimeSpan? initialIdle,
        TimeSpan? idleReturnThreshold = null)
    {
        _idleReturnThreshold = idleReturnThreshold ?? DefaultIdleReturnThreshold;
        if (_idleReturnThreshold < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleReturnThreshold));
        }

        _lastObservedAt = initialNow;
        _lastObservedIdle = initialIdle;
    }

    public CompanionEvent? Poll(
        DateTime now,
        TimeSpan? currentIdle,
        DateTime? nextStoryDueAt)
    {
        lock (_sync)
        {
            if (now < _lastObservedAt)
            {
                _lastObservedAt = now;
                _lastObservedIdle = currentIdle;
                return DequeueNext();
            }

            var dayChanged = now.Date != _lastObservedAt.Date;
            var hourChanged = dayChanged || now.Hour != _lastObservedAt.Hour;
            var storyDue = nextStoryDueAt is { } dueAt
                           && dueAt <= now
                           && dueAt != _lastEmittedStoryDueAt
                           && !_pending.Contains(CompanionEvent.StoryTimerDue);
            var idleReturned = _lastObservedIdle is { } previousIdle
                               && currentIdle is { } idle
                               && previousIdle >= _idleReturnThreshold
                               && idle < _idleReturnThreshold;

            _lastObservedAt = now;
            _lastObservedIdle = currentIdle;
            if (nextStoryDueAt is null && !_pending.Contains(CompanionEvent.StoryTimerDue))
            {
                _lastEmittedStoryDueAt = null;
            }

            if (dayChanged)
            {
                _pending.Add(CompanionEvent.DayChanged);
            }
            if (idleReturned)
            {
                _pending.Add(CompanionEvent.IdleReturned);
            }
            if (storyDue)
            {
                _pending.Add(CompanionEvent.StoryTimerDue);
                _pendingStoryDueAt = nextStoryDueAt;
            }
            if (hourChanged)
            {
                _pending.Add(CompanionEvent.ClockTick);
            }

            return DequeueNext();
        }
    }

    private CompanionEvent? DequeueNext()
    {
        foreach (var companionEvent in new[]
                 {
                     CompanionEvent.DayChanged,
                     CompanionEvent.IdleReturned,
                     CompanionEvent.StoryTimerDue,
                     CompanionEvent.ClockTick
                 })
        {
            if (!_pending.Remove(companionEvent))
            {
                continue;
            }

            if (companionEvent == CompanionEvent.StoryTimerDue)
            {
                _lastEmittedStoryDueAt = _pendingStoryDueAt;
                _pendingStoryDueAt = null;
            }

            return companionEvent;
        }

        return null;
    }
}
