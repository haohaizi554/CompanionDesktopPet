using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.UI;

internal readonly record struct AutomaticDialogueRuntimeSnapshot(
    bool IsScheduled,
    TimeSpan ScheduledDelay,
    AutomaticCadenceMode? ArmedMode,
    long ArmedAtTimestamp,
    FullscreenSnapshot Fullscreen);
