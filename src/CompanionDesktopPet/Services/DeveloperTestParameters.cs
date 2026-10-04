using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.Services;

public sealed class DeveloperTestParameters
{
    public const int MinimumDayMinutes = 1;
    public const int MaximumDayMinutes = 30;
    public const int MinimumEveningMinutes = 5;
    public const int MaximumEveningMinutes = 45;
    public const int MinimumLateNightMinutes = 15;
    public const int MaximumLateNightMinutes = 120;
    public const int MinimumFullscreenMinutes = 30;
    public const int MaximumFullscreenMinutes = 180;
    public const int MinimumBubbleSeconds = 2;
    public const int MaximumBubbleSeconds = 60;
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
    public const int MinimumReplyChars = 50;
    public const int MaximumReplyChars = 200;

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
    public double SpeechRepetition { get; set; } = 1.4;
    public int TopK { get; set; } = 15;
    public double TopP { get; set; } = 1;
    public int ReplyMaxChars { get; set; } = 80;

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
        TopP = TopP,
        ReplyMaxChars = ReplyMaxChars
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
        ReplyMaxChars = source.ReplyMaxChars;
    }

    public bool TryCopyTuning(PetTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var draft = Clone();
        (draft.DayMinimumMinutes, draft.DayMaximumMinutes) = OrderedBand(
            tuning.DayMinimumMinutes,
            tuning.DayMaximumMinutes,
            MinimumDayMinutes,
            MaximumDayMinutes);
        (draft.EveningMinimumMinutes, draft.EveningMaximumMinutes) = OrderedBand(
            tuning.EveningMinimumMinutes,
            tuning.EveningMaximumMinutes,
            MinimumEveningMinutes,
            MaximumEveningMinutes);
        (draft.LateNightMinimumMinutes, draft.LateNightMaximumMinutes) = OrderedBand(
            tuning.LateNightMinimumMinutes,
            tuning.LateNightMaximumMinutes,
            MinimumLateNightMinutes,
            MaximumLateNightMinutes);
        (draft.FullscreenMinimumMinutes, draft.FullscreenMaximumMinutes) = OrderedBand(
            tuning.FullscreenMinimumMinutes,
            tuning.FullscreenMaximumMinutes,
            MinimumFullscreenMinutes,
            MaximumFullscreenMinutes);
        draft.BubbleSeconds = int.Clamp(tuning.BubbleSeconds, MinimumBubbleSeconds, MaximumBubbleSeconds);
        draft.SpeechSpeed = SnapSpeechStep(
            FiniteBand(tuning.SpeechSpeed, draft.SpeechSpeed, MinimumSpeechSpeed, MaximumSpeechSpeed),
            MinimumSpeechSpeed,
            MaximumSpeechSpeed);
        draft.SpeechTemperature = SnapSpeechStep(
            FiniteBand(
                tuning.SpeechTemperature,
                draft.SpeechTemperature,
                MinimumSpeechTemperature,
                MaximumSpeechTemperature),
            MinimumSpeechTemperature,
            MaximumSpeechTemperature);
        draft.SpeechRepetition = SnapSpeechStep(
            FiniteBand(
                tuning.SpeechRepetition,
                draft.SpeechRepetition,
                MinimumSpeechRepetition,
                MaximumSpeechRepetition),
            MinimumSpeechRepetition,
            MaximumSpeechRepetition);
        draft.TopK = int.Clamp(tuning.TopK, MinimumTopK, MaximumTopK);
        draft.TopP = FiniteBand(tuning.TopP, draft.TopP, MinimumTopP, MaximumTopP);
        draft.ReplyMaxChars = tuning.ReplyMaxChars == 0
            ? 80
            : int.Clamp(tuning.ReplyMaxChars, MinimumReplyChars, MaximumReplyChars);
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
        ReplyMaxChars = ReplyMaxChars,
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
        var ranges = new (string Name, int Minimum, int Maximum, int Low, int High)[]
        {
            ("白天", DayMinimumMinutes, DayMaximumMinutes, MinimumDayMinutes, MaximumDayMinutes),
            ("傍晚", EveningMinimumMinutes, EveningMaximumMinutes, MinimumEveningMinutes, MaximumEveningMinutes),
            ("深夜", LateNightMinimumMinutes, LateNightMaximumMinutes, MinimumLateNightMinutes, MaximumLateNightMinutes),
            ("全屏", FullscreenMinimumMinutes, FullscreenMaximumMinutes, MinimumFullscreenMinutes, MaximumFullscreenMinutes)
        };
        foreach (var range in ranges)
        {
            if (range.Minimum < range.Low
                || range.Maximum < range.Low
                || range.Minimum > range.High
                || range.Maximum > range.High)
            {
                return $"{range.Name}间隔要在 {range.Low} 到 {range.High} 分钟之间。";
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

        if (ReplyMaxChars < MinimumReplyChars || ReplyMaxChars > MaximumReplyChars)
        {
            return $"输出要在 {MinimumReplyChars} 到 {MaximumReplyChars} 字之间。";
        }

        return null;
    }

    private static (int Minimum, int Maximum) OrderedBand(int minimum, int maximum, int low, int high)
    {
        minimum = int.Clamp(minimum, low, high);
        maximum = int.Clamp(maximum, low, high);
        if (minimum > maximum)
        {
            maximum = minimum;
        }

        return (minimum, maximum);
    }

    private static double FiniteBand(double value, double fallback, double minimum, double maximum) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : double.Clamp(value, minimum, maximum);

    internal static double SnapSpeechStep(double value, double minimum, double maximum)
    {
        const double step = 0.1;
        value = Math.Clamp(value, minimum, maximum);
        var snapped = minimum + Math.Round((value - minimum) / step, MidpointRounding.AwayFromZero) * step;
        snapped = Math.Round(snapped * 10d, MidpointRounding.AwayFromZero) / 10d;
        return Math.Clamp(snapped, minimum, maximum);
    }

    private static bool InSpeechRange(double value, double minimum, double maximum) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum;
}
