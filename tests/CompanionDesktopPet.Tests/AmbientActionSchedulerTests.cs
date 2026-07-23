using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class AmbientActionSchedulerTests
{
    [Theory]
    [InlineData(0.0, 3.2)]
    [InlineData(0.5, 5.0)]
    [InlineData(1.0, 6.8)]
    public void NextBlinkDelay_MapsSamplesIntoNaturalBounds(double sample, double seconds)
    {
        var scheduler = new AmbientActionScheduler(() => sample);

        Assert.Equal(TimeSpan.FromSeconds(seconds), scheduler.NextBlinkDelay());
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.124, true)]
    [InlineData(0.125, false)]
    [InlineData(1.0, false)]
    public void ShouldDoubleBlink_UsesOneInEightThreshold(double sample, bool expected)
    {
        Assert.Equal(expected, new AmbientActionScheduler(() => sample).ShouldDoubleBlink());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void NextBlinkDelay_RejectsInvalidSamples(double sample)
    {
        var scheduler = new AmbientActionScheduler(() => sample);

        Assert.Throws<InvalidOperationException>(() => { scheduler.NextBlinkDelay(); });
    }
}
