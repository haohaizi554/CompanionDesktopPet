namespace CompanionDesktopPet.Services;

public sealed class DialogueScheduler
{
    private readonly Random _random;

    public DialogueScheduler(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public TimeSpan NextDelay() => NextDelay(DateTime.Now);

    public TimeSpan NextDelay(DateTime localTime, bool isFullscreen = false)
    {
        if (isFullscreen)
        {
            return TimeSpan.FromSeconds(_random.Next(90 * 60, (150 * 60) + 1));
        }

        var period = TemporalDialogueService.GetTimePeriod(localTime);
        return period is TimePeriod.LateNight or TimePeriod.Dawn
            ? TimeSpan.FromSeconds(_random.Next(45 * 60, (90 * 60) + 1))
            : TimeSpan.FromSeconds(_random.Next(20 * 60, (50 * 60) + 1));
    }
}
