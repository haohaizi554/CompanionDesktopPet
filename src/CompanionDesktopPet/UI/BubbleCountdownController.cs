using System.Diagnostics;
using System.Windows;

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
    HoverPaused,
    Suspended
}

/// <summary>Tracks the visible speech bubble's five-second countdown.</summary>
/// <remarks>
/// This controller is intentionally not thread-safe. Construct and call it only from the
/// owning WPF UI dispatcher; debug builds assert that contract at every public state entry.
/// </remarks>
public sealed class BubbleCountdownController
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _timeProvider;
    private long _startedAt;
    private TimeSpan _remaining;
    private bool _closed;
    private bool _suspended;

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
        AssertUiThread();
        if (_closed) return;
        _remaining = DisplayDuration;
        if (_suspended)
        {
            State = BubbleCountdownState.Suspended;
            return;
        }

        if (HoverTargets != BubbleHoverTarget.None)
        {
            State = BubbleCountdownState.HoverPaused;
            return;
        }

        StartCounting();
    }

    public void Enter(BubbleHoverTarget target)
    {
        AssertUiThread();
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
        AssertUiThread();
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
        AssertUiThread();
        if (_closed
            || State != BubbleCountdownState.CountingDown
            || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        Hide();
        return true;
    }

    public void Suspend()
    {
        AssertUiThread();
        if (_closed || _suspended)
        {
            return;
        }

        if (State == BubbleCountdownState.CountingDown)
        {
            _remaining = Remaining;
        }

        _suspended = true;
        State = BubbleCountdownState.Suspended;
    }

    public void Resume()
    {
        AssertUiThread();
        if (_closed || !_suspended)
        {
            return;
        }

        _suspended = false;
        if (State != BubbleCountdownState.Suspended)
        {
            return;
        }

        if (_remaining <= TimeSpan.Zero)
        {
            Hide();
        }
        else if (HoverTargets != BubbleHoverTarget.None)
        {
            State = BubbleCountdownState.HoverPaused;
        }
        else
        {
            StartCounting();
        }
    }

    public void Hide()
    {
        AssertUiThread();
        State = BubbleCountdownState.Hidden;
        _remaining = TimeSpan.Zero;
    }

    public void Close()
    {
        AssertUiThread();
        _closed = true;
        _suspended = false;
        HoverTargets = BubbleHoverTarget.None;
        Hide();
    }

    private void StartCounting()
    {
        _startedAt = _timeProvider.GetTimestamp();
        State = BubbleCountdownState.CountingDown;
    }

    [Conditional("DEBUG")]
    private static void AssertUiThread() =>
        Debug.Assert(
            Application.Current is null || Application.Current.Dispatcher.CheckAccess(),
            "BubbleCountdownController must only be used by the owning UI dispatcher.");
}
