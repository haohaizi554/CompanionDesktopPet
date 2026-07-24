using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class BubbleCountdownControllerTests
{
    [Fact]
    public void HoverPausesRemainingTimeUntilTheLastTargetLeaves()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);

        countdown.Show();
        time.Advance(TimeSpan.FromSeconds(2));
        countdown.Enter(BubbleHoverTarget.Character);
        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);

        countdown.Enter(BubbleHoverTarget.Bubble);
        time.Advance(TimeSpan.FromSeconds(100));
        countdown.Leave(BubbleHoverTarget.Character);
        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);

        countdown.Leave(BubbleHoverTarget.Bubble);
        Assert.Equal(BubbleCountdownState.CountingDown, countdown.State);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True(countdown.TryExpire());
        Assert.Equal(BubbleCountdownState.Hidden, countdown.State);
    }

    [Fact]
    public void NewMessageWhileHoveredResetsToPausedFiveSeconds()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);
        countdown.Enter(BubbleHoverTarget.Character);
        countdown.Show();
        time.Advance(TimeSpan.FromMinutes(1));
        countdown.Show();

        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(5), countdown.Remaining);
        Assert.False(countdown.TryExpire());
    }

    [Fact]
    public void NewMessageRejectsThePriorCountdownsStaleExpiry()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);

        countdown.Show();
        time.Advance(TimeSpan.FromSeconds(4));
        countdown.Show();
        time.Advance(TimeSpan.FromMilliseconds(1100));

        Assert.False(countdown.TryExpire());
        Assert.Equal(BubbleCountdownState.CountingDown, countdown.State);
        Assert.Equal(TimeSpan.FromMilliseconds(3900), countdown.Remaining);
    }

    [Fact]
    public void HideAndCloseCannotBeRevivedByLeaveOrShow()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);
        countdown.Enter(BubbleHoverTarget.Character);
        countdown.Show();
        countdown.Hide();
        countdown.Leave(BubbleHoverTarget.Character);
        Assert.Equal(BubbleCountdownState.Hidden, countdown.State);

        countdown.Close();
        countdown.Show();
        Assert.Equal(BubbleCountdownState.Hidden, countdown.State);
        Assert.False(countdown.TryExpire());
    }

    [Fact]
    public void SuspendFreezesTheDeadlineUntilResume()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);

        countdown.Show();
        time.Advance(TimeSpan.FromSeconds(2));
        countdown.Suspend();

        Assert.Equal(BubbleCountdownState.Suspended, countdown.State);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);
        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);
        Assert.False(countdown.TryExpire());

        countdown.Resume();
        Assert.Equal(BubbleCountdownState.CountingDown, countdown.State);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.True(countdown.TryExpire());
    }

    [Fact]
    public void SuspendBeforeFirstMessageKeepsTheNewMessagePausedUntilResume()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);

        countdown.Suspend();
        countdown.Show();
        time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(BubbleCountdownState.Suspended, countdown.State);
        Assert.Equal(BubbleCountdownController.DisplayDuration, countdown.Remaining);
        Assert.False(countdown.TryExpire());

        countdown.Resume();

        Assert.Equal(BubbleCountdownState.CountingDown, countdown.State);
        Assert.Equal(BubbleCountdownController.DisplayDuration, countdown.Remaining);
    }

    [Fact]
    public void ResumeReturnsToHoverPausedUntilTheLastTargetLeaves()
    {
        var time = new ManualTimeProvider();
        var countdown = new BubbleCountdownController(time);
        countdown.Enter(BubbleHoverTarget.Character);
        countdown.Show();

        countdown.Suspend();
        time.Advance(TimeSpan.FromHours(1));
        countdown.Resume();

        Assert.Equal(BubbleCountdownState.HoverPaused, countdown.State);
        Assert.Equal(BubbleCountdownController.DisplayDuration, countdown.Remaining);
        Assert.False(countdown.TryExpire());

        countdown.Leave(BubbleHoverTarget.Character);

        Assert.Equal(BubbleCountdownState.CountingDown, countdown.State);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
