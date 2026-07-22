namespace CompanionDesktopPet.Services;

public sealed class DialogueScheduler
{
    private readonly Random _random;

    public DialogueScheduler(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public TimeSpan NextDelay() => TimeSpan.FromSeconds(_random.Next(300, 601));
}
