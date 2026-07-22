using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueSchedulerTests
{
    [Fact]
    public void NextDelay_IsAlwaysBetweenFiveAndTenMinutes()
    {
        var scheduler = new DialogueScheduler(new Random(42));

        for (var index = 0; index < 100; index++)
        {
            var delay = scheduler.NextDelay();
            Assert.InRange(delay, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));
        }
    }
}
