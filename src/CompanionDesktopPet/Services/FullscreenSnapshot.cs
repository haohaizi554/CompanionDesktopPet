namespace CompanionDesktopPet.Services;

internal readonly record struct FullscreenSnapshot(bool? Observed, bool EffectiveQuietMode);

internal sealed class FullscreenStateTracker
{
    private bool? _lastExplicit;

    internal FullscreenSnapshot Update(bool? observed)
    {
        if (observed.HasValue)
        {
            _lastExplicit = observed.Value;
        }

        return new FullscreenSnapshot(observed, _lastExplicit is true);
    }
}
