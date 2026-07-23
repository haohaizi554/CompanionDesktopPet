namespace CompanionDesktopPet.Services;

public sealed class AmbientActionScheduler
{
    private readonly Func<double> _sample;
    private readonly TimeProvider _timeProvider;

    public AmbientActionScheduler(Func<double>? sample = null)
        : this(sample, TimeProvider.System)
    {
    }

    public AmbientActionScheduler(Func<double>? sample, TimeProvider timeProvider)
    {
        _sample = sample ?? Random.Shared.NextDouble;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public TimeSpan NextBlinkDelay() =>
        TimeSpan.FromSeconds(3.2 + (3.6 * NextSample()));

    public bool ShouldDoubleBlink() => NextSample() < 0.125;

    internal long GetDeadline(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        var timestampDelta = checked((long)Math.Ceiling(
            delay.TotalSeconds * _timeProvider.TimestampFrequency));
        return checked(_timeProvider.GetTimestamp() + timestampDelta);
    }

    internal TimeSpan GetRemaining(long deadline)
    {
        var now = _timeProvider.GetTimestamp();
        return deadline <= now
            ? TimeSpan.Zero
            : _timeProvider.GetElapsedTime(now, deadline);
    }

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
