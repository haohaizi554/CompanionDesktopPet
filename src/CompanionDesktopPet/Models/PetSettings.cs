namespace CompanionDesktopPet.Models;

public enum PetScale
{
    Small,
    Normal,
    Large
}

public sealed record PetSettings(
    double Left,
    double Top,
    PetScale Scale,
    bool AnimationPaused,
    bool AlwaysOnTop)
{
    public static PetSettings Default { get; } =
        new(double.NaN, double.NaN, PetScale.Normal, false, true);
}
