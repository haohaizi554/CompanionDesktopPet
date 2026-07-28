using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class DialogueSchedulerTests
{
    [Fact]
    public void PublicApi_UsesTheThreadSafeSharedRandomPath()
    {
        Assert.NotNull(typeof(DialogueScheduler).GetConstructor(Type.EmptyTypes));
        Assert.Null(typeof(DialogueScheduler).GetConstructor([typeof(Random)]));
    }

    [Theory]
    [InlineData(3, 59, 59, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
    [InlineData(4, 0, 0, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
    [InlineData(5, 59, 59, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
    [InlineData(6, 0, 0, AutomaticCadenceMode.Daytime, 5, 15)]
    [InlineData(10, 59, 59, AutomaticCadenceMode.Daytime, 5, 15)]
    [InlineData(11, 0, 0, AutomaticCadenceMode.Daytime, 5, 15)]
    [InlineData(13, 59, 59, AutomaticCadenceMode.Daytime, 5, 15)]
    [InlineData(14, 0, 0, AutomaticCadenceMode.Daytime, 5, 15)]
    [InlineData(17, 59, 59, AutomaticCadenceMode.Daytime, 5, 15)]
    [InlineData(18, 0, 0, AutomaticCadenceMode.Evening, 10, 20)]
    [InlineData(22, 59, 59, AutomaticCadenceMode.Evening, 10, 20)]
    [InlineData(23, 0, 0, AutomaticCadenceMode.LateNightOrDawn, 30, 60)]
    public void NextDelay_UsesCanonicalInclusiveBoundaries(
        int hour, int minute, int second,
        object expectedMode,
        int minimumMinutes, int maximumMinutes)
    {
        var at = new DateTime(2026, 7, 26, hour, minute, second);
        Assert.Equal((AutomaticCadenceMode)expectedMode, DialogueScheduler.GetMode(at, false));
        Assert.Equal(TimeSpan.FromMinutes(minimumMinutes),
            new DialogueScheduler(new EndpointRandom(false).Next).NextDelay(at));
        Assert.Equal(TimeSpan.FromMinutes(maximumMinutes),
            new DialogueScheduler(new EndpointRandom(true).Next).NextDelay(at));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(18)]
    [InlineData(23)]
    public void NextDelay_UsesFullscreenCadenceWhenEffectiveQuietModeIsEnabled(int hour)
    {
        var at = new DateTime(2026, 7, 26, hour, 0, 0);
        Assert.Equal(AutomaticCadenceMode.Fullscreen, DialogueScheduler.GetMode(at, true));
        Assert.Equal(TimeSpan.FromMinutes(60),
            new DialogueScheduler(new EndpointRandom(false).Next).NextDelay(at, effectiveQuietMode: true));
        Assert.Equal(TimeSpan.FromMinutes(120),
            new DialogueScheduler(new EndpointRandom(true).Next).NextDelay(at, effectiveQuietMode: true));
    }

    private sealed class EndpointRandom(bool maximum) : Random
    {
        public override int Next(int minValue, int maxValue) =>
            maximum ? maxValue - 1 : minValue;
    }
}
