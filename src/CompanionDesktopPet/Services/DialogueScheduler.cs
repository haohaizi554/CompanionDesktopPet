namespace CompanionDesktopPet.Services;

internal enum AutomaticCadenceMode
{
    Daytime,
    Evening,
    LateNightOrDawn,
    Fullscreen
}

public sealed class DialogueScheduler
{
    private readonly Func<int, int, int> _next;

    public DialogueScheduler()
        : this(Random.Shared.Next)
    {
    }

    internal DialogueScheduler(Func<int, int, int> next) =>
        _next = next ?? throw new ArgumentNullException(nameof(next));

    [Obsolete("使用 NextDelay(DateTime, bool) 重载以显式传入 effectiveQuietMode")]
    public TimeSpan NextDelay() => NextDelay(DateTime.Now);

    internal static AutomaticCadenceMode GetMode(DateTime localTime, bool effectiveQuietMode)
    {
        if (effectiveQuietMode)
        {
            return AutomaticCadenceMode.Fullscreen;
        }

        return TemporalDialogueService.GetTimePeriod(localTime) switch
        {
            TimePeriod.Evening => AutomaticCadenceMode.Evening,
            TimePeriod.LateNight or TimePeriod.Dawn => AutomaticCadenceMode.LateNightOrDawn,
            _ => AutomaticCadenceMode.Daytime
        };
    }

    public TimeSpan NextDelay(DateTime localTime, bool effectiveQuietMode = false)
    {
        var (minimum, maximum) = GetMode(localTime, effectiveQuietMode) switch
        {
            AutomaticCadenceMode.Daytime => (5, 15),
            AutomaticCadenceMode.Evening => (10, 20),
            AutomaticCadenceMode.LateNightOrDawn => (30, 60),
            AutomaticCadenceMode.Fullscreen => (60, 120),
            _ => throw new ArgumentOutOfRangeException()
        };
        return TimeSpan.FromSeconds(_next(minimum * 60, maximum * 60 + 1));
    }
}
