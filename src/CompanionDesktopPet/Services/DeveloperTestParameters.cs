namespace CompanionDesktopPet.Services;

public sealed class DeveloperTestParameters
{
    public const int MinimumMinutes = 0;
    public const int MaximumMinutes = 24 * 60;
    public const int MinimumBubbleSeconds = 1;
    public const int MaximumBubbleSeconds = 120;

    public int DayMinimumMinutes { get; set; } = 5;
    public int DayMaximumMinutes { get; set; } = 15;
    public int EveningMinimumMinutes { get; set; } = 10;
    public int EveningMaximumMinutes { get; set; } = 20;
    public int LateNightMinimumMinutes { get; set; } = 30;
    public int LateNightMaximumMinutes { get; set; } = 60;
    public int FullscreenMinimumMinutes { get; set; } = 60;
    public int FullscreenMaximumMinutes { get; set; } = 120;
    public int BubbleSeconds { get; set; } = 5;

    public static DeveloperTestParameters CreateDefault() => new();

    public DeveloperTestParameters Clone() => new()
    {
        DayMinimumMinutes = DayMinimumMinutes,
        DayMaximumMinutes = DayMaximumMinutes,
        EveningMinimumMinutes = EveningMinimumMinutes,
        EveningMaximumMinutes = EveningMaximumMinutes,
        LateNightMinimumMinutes = LateNightMinimumMinutes,
        LateNightMaximumMinutes = LateNightMaximumMinutes,
        FullscreenMinimumMinutes = FullscreenMinimumMinutes,
        FullscreenMaximumMinutes = FullscreenMaximumMinutes,
        BubbleSeconds = BubbleSeconds
    };

    public void CopyFrom(DeveloperTestParameters source)
    {
        ArgumentNullException.ThrowIfNull(source);
        DayMinimumMinutes = source.DayMinimumMinutes;
        DayMaximumMinutes = source.DayMaximumMinutes;
        EveningMinimumMinutes = source.EveningMinimumMinutes;
        EveningMaximumMinutes = source.EveningMaximumMinutes;
        LateNightMinimumMinutes = source.LateNightMinimumMinutes;
        LateNightMaximumMinutes = source.LateNightMaximumMinutes;
        FullscreenMinimumMinutes = source.FullscreenMinimumMinutes;
        FullscreenMaximumMinutes = source.FullscreenMaximumMinutes;
        BubbleSeconds = source.BubbleSeconds;
    }

    internal (int Minimum, int Maximum) Window(AutomaticCadenceMode mode) => mode switch
    {
        AutomaticCadenceMode.Daytime => (DayMinimumMinutes, DayMaximumMinutes),
        AutomaticCadenceMode.Evening => (EveningMinimumMinutes, EveningMaximumMinutes),
        AutomaticCadenceMode.LateNightOrDawn => (LateNightMinimumMinutes, LateNightMaximumMinutes),
        AutomaticCadenceMode.Fullscreen => (FullscreenMinimumMinutes, FullscreenMaximumMinutes),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public string? Validate()
    {
        var ranges = new (string Name, int Minimum, int Maximum)[]
        {
            ("白天", DayMinimumMinutes, DayMaximumMinutes),
            ("傍晚", EveningMinimumMinutes, EveningMaximumMinutes),
            ("深夜", LateNightMinimumMinutes, LateNightMaximumMinutes),
            ("全屏", FullscreenMinimumMinutes, FullscreenMaximumMinutes)
        };
        foreach (var range in ranges)
        {
            if (range.Minimum < MinimumMinutes
                || range.Maximum < MinimumMinutes
                || range.Minimum > MaximumMinutes
                || range.Maximum > MaximumMinutes)
            {
                return $"{range.Name}间隔要在 {MinimumMinutes} 到 {MaximumMinutes} 分钟之间。";
            }

            if (range.Minimum > range.Maximum)
            {
                return $"{range.Name}的最短间隔不能大于最长间隔。";
            }
        }

        if (BubbleSeconds < MinimumBubbleSeconds || BubbleSeconds > MaximumBubbleSeconds)
        {
            return $"气泡停留要在 {MinimumBubbleSeconds} 到 {MaximumBubbleSeconds} 秒之间。";
        }

        return null;
    }
}
