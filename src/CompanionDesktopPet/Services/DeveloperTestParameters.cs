using CompanionDesktopPet.Models;

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
    public const int MinimumTopK = 1;
    public const int MaximumTopK = 50;
    public const double MinimumTopP = 0.1;
    public const double MaximumTopP = 1;

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
    public int TopK { get; set; } = 15;
    public double TopP { get; set; } = 1;

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
        SpeechRepetition = SpeechRepetition,
        TopK = TopK,
        TopP = TopP
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
        TopK = source.TopK;
        TopP = source.TopP;
    }

    public bool TryCopyTuning(PetTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var draft = Clone();
        draft.DayMinimumMinutes = tuning.DayMinimumMinutes;
        draft.DayMaximumMinutes = tuning.DayMaximumMinutes;
        draft.EveningMinimumMinutes = tuning.EveningMinimumMinutes;
        draft.EveningMaximumMinutes = tuning.EveningMaximumMinutes;
        draft.LateNightMinimumMinutes = tuning.LateNightMinimumMinutes;
        draft.LateNightMaximumMinutes = tuning.LateNightMaximumMinutes;
        draft.FullscreenMinimumMinutes = tuning.FullscreenMinimumMinutes;
        draft.FullscreenMaximumMinutes = tuning.FullscreenMaximumMinutes;
        draft.BubbleSeconds = tuning.BubbleSeconds;
        draft.SpeechSpeed = tuning.SpeechSpeed;
        draft.SpeechTemperature = tuning.SpeechTemperature;
        draft.SpeechRepetition = tuning.SpeechRepetition;
        draft.TopK = tuning.TopK;
        draft.TopP = tuning.TopP;
        if (draft.Validate() is not null)
        {
            return false;
        }

        CopyFrom(draft);
        return true;
    }

    public PetTuning ToTuning(bool voiceEnabled) => new()
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
        SpeechRepetition = SpeechRepetition,
        TopK = TopK,
        TopP = TopP,
        VoiceEnabled = voiceEnabled
    };

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

        if (TopK < MinimumTopK || TopK > MaximumTopK)
        {
            return $"采样个数要在 {MinimumTopK} 到 {MaximumTopK} 之间。";
        }

        if (!InSpeechRange(TopP, MinimumTopP, MaximumTopP))
        {
            return $"采样范围要在 {MinimumTopP:0.#} 到 {MaximumTopP:0.#} 之间。";
        }

        return null;
    }

    private static bool InSpeechRange(double value, double minimum, double maximum) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
}
