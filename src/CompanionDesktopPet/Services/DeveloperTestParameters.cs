namespace CompanionDesktopPet.Services;

public sealed class DeveloperTestParameters
{
    public const int MinimumMinutes = 0;
    public const int MaximumMinutes = 24 * 60;
    public const int MinimumBubbleSeconds = 1;
    public const int MaximumBubbleSeconds = 120;
    public const double MinimumSpeechSpeed = 0.5;
    public const double MaximumSpeechSpeed = 2;
    public const double MinimumSpeechTemperature = 0.2;
    public const double MaximumSpeechTemperature = 1.5;
    public const double MinimumSpeechRepetition = 1;
    public const double MaximumSpeechRepetition = 2;

    public int DayMinimumMinutes { get; set; } = 5;
    public int DayMaximumMinutes { get; set; } = 15;
    public int EveningMinimumMinutes { get; set; } = 10;
    public int EveningMaximumMinutes { get; set; } = 20;
    public int LateNightMinimumMinutes { get; set; } = 30;
    public int LateNightMaximumMinutes { get; set; } = 60;
    public int FullscreenMinimumMinutes { get; set; } = 60;
    public int FullscreenMaximumMinutes { get; set; } = 120;
    public int BubbleSeconds { get; set; } = 5;
    public double SpeechSpeed { get; set; } = 1;
    public double SpeechTemperature { get; set; } = 1;
    public double SpeechRepetition { get; set; } = 1.35;

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
        BubbleSeconds = BubbleSeconds,
        SpeechSpeed = SpeechSpeed,
        SpeechTemperature = SpeechTemperature,
        SpeechRepetition = SpeechRepetition
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
        SpeechSpeed = source.SpeechSpeed;
        SpeechTemperature = source.SpeechTemperature;
        SpeechRepetition = source.SpeechRepetition;
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

        if (!InSpeechRange(SpeechSpeed, MinimumSpeechSpeed, MaximumSpeechSpeed))
        {
            return $"语速要在 {MinimumSpeechSpeed:0.#} 到 {MaximumSpeechSpeed:0.#} 倍之间。";
        }

        if (!InSpeechRange(SpeechTemperature, MinimumSpeechTemperature, MaximumSpeechTemperature))
        {
            return $"语气要在 {MinimumSpeechTemperature:0.#} 到 {MaximumSpeechTemperature:0.#} 之间。";
        }

        if (!InSpeechRange(SpeechRepetition, MinimumSpeechRepetition, MaximumSpeechRepetition))
        {
            return $"重复要在 {MinimumSpeechRepetition:0.#} 到 {MaximumSpeechRepetition:0.#} 之间。";
        }

        return null;
    }

    private static bool InSpeechRange(double value, double minimum, double maximum) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
}
