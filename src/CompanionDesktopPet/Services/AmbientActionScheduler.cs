namespace CompanionDesktopPet.Services;

public sealed class AmbientActionScheduler(Func<double>? sample = null)
{
    private readonly Func<double> _sample = sample ?? Random.Shared.NextDouble;

    public TimeSpan NextBlinkDelay() =>
        TimeSpan.FromSeconds(3.2 + (3.6 * NextSample()));

    public bool ShouldDoubleBlink() => NextSample() < 0.125;

    private double NextSample()
    {
        var value = _sample();
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new InvalidOperationException(
                "Random samples must be finite values from zero through one.");
        }

        return value;
    }
}
