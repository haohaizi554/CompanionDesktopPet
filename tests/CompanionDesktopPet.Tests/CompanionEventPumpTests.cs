using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class CompanionEventPumpTests
{
    [Fact]
    public void Poll_ConcurrentMidnightStoryIdleAndClockEvents_QueuesEveryEventInStablePriority()
    {
        var beforeMidnight = new DateTime(2026, 7, 22, 23, 59, 45, DateTimeKind.Local);
        var dueAt = beforeMidnight.AddSeconds(20);
        var pump = new CompanionEventPump(beforeMidnight, TimeSpan.FromMinutes(12));

        var now = beforeMidnight.AddSeconds(30);
        var events = Enumerable.Range(0, 4)
            .Select(_ => pump.Poll(now, TimeSpan.FromSeconds(5), dueAt))
            .ToArray();

        Assert.Equal(
            new CompanionEvent?[]
            {
                CompanionEvent.DayChanged,
                CompanionEvent.IdleReturned,
                CompanionEvent.StoryTimerDue,
                CompanionEvent.ClockTick
            },
            events);
    }

    [Fact]
    public void Poll_SameDueStoryIsEmittedOnce_AndANewDueStoryIsNotLost()
    {
        var start = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Local);
        var firstDue = start.AddSeconds(5);
        var secondDue = start.AddSeconds(15);
        var pump = new CompanionEventPump(start, TimeSpan.Zero);

        Assert.Equal(
            CompanionEvent.StoryTimerDue,
            pump.Poll(start.AddSeconds(10), TimeSpan.FromSeconds(10), firstDue));
        Assert.Null(pump.Poll(start.AddSeconds(12), TimeSpan.FromSeconds(12), firstDue));
        Assert.Equal(
            CompanionEvent.StoryTimerDue,
            pump.Poll(start.AddSeconds(20), TimeSpan.FromSeconds(20), secondDue));
    }

    [Fact]
    public void Poll_IdleReturnRequiresAConfirmedThresholdCrossing()
    {
        var start = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Local);
        var pump = new CompanionEventPump(start, TimeSpan.FromMinutes(2));

        Assert.Null(pump.Poll(start.AddSeconds(30), TimeSpan.FromSeconds(3), null));
        Assert.Null(pump.Poll(start.AddMinutes(6), TimeSpan.FromMinutes(7), null));
        Assert.Equal(
            CompanionEvent.IdleReturned,
            pump.Poll(start.AddMinutes(6).AddSeconds(30), TimeSpan.FromSeconds(4), null));
    }
}
