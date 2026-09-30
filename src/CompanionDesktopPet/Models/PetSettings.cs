using System.Text.Json.Serialization;

namespace CompanionDesktopPet.Models;

public enum PetScale
{
    Small,
    Normal,
    Large
}

public sealed record PetSettings(
    [property: JsonRequired] double Left,
    [property: JsonRequired] double Top,
    [property: JsonRequired] PetScale Scale,
    [property: JsonRequired] bool AnimationPaused,
    [property: JsonRequired] bool AlwaysOnTop)
{
    public const double MaximumCoordinateMagnitude = 1_000_000;

    public bool DialogueEnabled { get; init; }

    public PetTuning? Tuning { get; init; }

    public static PetSettings Default { get; } =
        new(double.NaN, double.NaN, PetScale.Normal, false, true);

    public static bool IsValid(PetSettings? settings) =>
        settings is not null
        && IsCoordinate(settings.Left)
        && IsCoordinate(settings.Top)
        && Enum.IsDefined(settings.Scale);

    private static bool IsCoordinate(double value) =>
        double.IsFinite(value)
        && Math.Abs(value) <= MaximumCoordinateMagnitude;
}

public sealed record PetTuning
{
    public int DayMinimumMinutes { get; init; } = 5;
    public int DayMaximumMinutes { get; init; } = 15;
    public int EveningMinimumMinutes { get; init; } = 10;
    public int EveningMaximumMinutes { get; init; } = 20;
    public int LateNightMinimumMinutes { get; init; } = 30;
    public int LateNightMaximumMinutes { get; init; } = 60;
    public int FullscreenMinimumMinutes { get; init; } = 60;
    public int FullscreenMaximumMinutes { get; init; } = 120;
    public int BubbleSeconds { get; init; } = 5;
    public double SpeechSpeed { get; init; } = 1;
    public double SpeechTemperature { get; init; } = 1;
    public double SpeechRepetition { get; init; } = 1.35;
    public int TopK { get; init; } = 15;
    public double TopP { get; init; } = 1;
    public int ReplyMaxChars { get; init; } = 80;
    public bool VoiceEnabled { get; init; } = true;
}
