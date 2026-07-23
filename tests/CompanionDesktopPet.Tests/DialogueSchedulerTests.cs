using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueSchedulerTests
{
    [Fact]
    public void NextDelay_UsesAQuietOrdinaryCadence()
    {
        var scheduler = new DialogueScheduler(new Random(42));

        for (var index = 0; index < 100; index++)
        {
            var delay = scheduler.NextDelay(new DateTime(2026, 7, 22, 15, 0, 0));
            Assert.InRange(delay, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(50));
        }
    }

    [Fact]
    public void NextDelay_IsQuieterLateAtNightOrInFullscreen()
    {
        var scheduler = new DialogueScheduler(new Random(42));

        for (var index = 0; index < 100; index++)
        {
            Assert.InRange(
                scheduler.NextDelay(new DateTime(2026, 7, 22, 1, 0, 0)),
                TimeSpan.FromMinutes(45),
                TimeSpan.FromMinutes(90));
            Assert.InRange(
                scheduler.NextDelay(new DateTime(2026, 7, 22, 15, 0, 0), isFullscreen: true),
                TimeSpan.FromMinutes(90),
                TimeSpan.FromMinutes(150));
        }
    }

    [Theory]
    [InlineData(4, 45, 90)]
    [InlineData(5, 45, 90)]
    [InlineData(6, 20, 50)]
    [InlineData(10, 20, 50)]
    public void NextDelay_UsesTheSharedDawnAndMorningBoundaries(int hour, int minimumMinutes, int maximumMinutes)
    {
        var scheduler = new DialogueScheduler(new Random(42));

        for (var index = 0; index < 50; index++)
        {
            Assert.InRange(
                scheduler.NextDelay(new DateTime(2026, 7, 22, hour, 0, 0)),
                TimeSpan.FromMinutes(minimumMinutes),
                TimeSpan.FromMinutes(maximumMinutes));
        }
    }
}
