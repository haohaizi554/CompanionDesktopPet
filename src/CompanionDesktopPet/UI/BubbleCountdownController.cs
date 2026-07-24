namespace CompanionDesktopPet.UI;

[Flags]
public enum BubbleHoverTarget
{
    None = 0,
    Character = 1,
    Bubble = 2
}

public enum BubbleCountdownState
{
    Hidden,
    CountingDown,
    HoverPaused
}

public sealed class BubbleCountdownController
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _timeProvider;
    private long _startedAt;
    private TimeSpan _remaining;
    private bool _closed;

    public BubbleCountdownController(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public BubbleCountdownState State { get; private set; }
    public BubbleHoverTarget HoverTargets { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (State != BubbleCountdownState.CountingDown)
            {
                return _remaining;
            }

            var elapsed = _timeProvider.GetElapsedTime(
                _startedAt,
                _timeProvider.GetTimestamp());
            return elapsed >= _remaining ? TimeSpan.Zero : _remaining - elapsed;
        }
    }

    public void Show()
    {
        if (_closed) return;
        _remaining = DisplayDuration;
        if (HoverTargets != BubbleHoverTarget.None)
        {
            State = BubbleCountdownState.HoverPaused;
            return;
        }

        StartCounting();
    }

    public void Enter(BubbleHoverTarget target)
    {
        if (_closed || target == BubbleHoverTarget.None) return;
        var wasClear = HoverTargets == BubbleHoverTarget.None;
        HoverTargets |= target;
        if (wasClear && State == BubbleCountdownState.CountingDown)
        {
            _remaining = Remaining;
            State = BubbleCountdownState.HoverPaused;
        }
    }

    public void Leave(BubbleHoverTarget target)
    {
        if (_closed || target == BubbleHoverTarget.None) return;
        HoverTargets &= ~target;
        if (HoverTargets == BubbleHoverTarget.None
            && State == BubbleCountdownState.HoverPaused)
        {
            if (_remaining <= TimeSpan.Zero)
            {
                Hide();
            }
            else
            {
                StartCounting();
            }
        }
    }

    public bool TryExpire()
    {
        if (_closed
            || State != BubbleCountdownState.CountingDown
            || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        Hide();
        return true;
    }

    public void Hide()
    {
        State = BubbleCountdownState.Hidden;
        _remaining = TimeSpan.Zero;
    }

    public void Close()
    {
        _closed = true;
        HoverTargets = BubbleHoverTarget.None;
        Hide();
    }

    private void StartCounting()
    {
        _startedAt = _timeProvider.GetTimestamp();
        State = BubbleCountdownState.CountingDown;
    }
}
