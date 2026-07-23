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
