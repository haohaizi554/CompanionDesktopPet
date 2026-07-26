namespace CompanionDesktopPet.Services;

internal interface IForegroundFullscreenDetector
{
    bool? Observe(nint excludedWindow);
}
