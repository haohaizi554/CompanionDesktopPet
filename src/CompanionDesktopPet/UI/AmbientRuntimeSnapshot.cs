using CompanionDesktopPet.Models;

namespace CompanionDesktopPet.UI;

internal enum StartupGreetingPhase
{
    Pending,
    Scheduled,
    Running,
    Completed
}

internal readonly record struct AmbientRuntimeSnapshot(
    PetActionState ActionState,
    StartupGreetingPhase StartupGreeting,
    bool IsScheduled,
    TimeSpan ScheduledDelay,
    PetAmbientAction PendingAction);
