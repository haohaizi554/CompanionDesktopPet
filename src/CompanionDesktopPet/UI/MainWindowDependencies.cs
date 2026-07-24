using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using System.Windows;

namespace CompanionDesktopPet.UI;

/// <summary>
/// Defines the runtime collaborators used by <see cref="MainWindow"/> without
/// exposing test-only injection seams as an ever-growing constructor signature.
/// </summary>
internal sealed class MainWindowDependencies
{
    internal MainWindowDependencies(PetSettings settings, SettingsService settingsService)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        SettingsService = settingsService
            ?? throw new ArgumentNullException(nameof(settingsService));
    }

    internal PetSettings Settings { get; }
    internal SettingsService SettingsService { get; }
    internal AgentMemoryService? AgentMemoryService { get; init; }
    internal AgentMemorySnapshot? AgentMemory { get; init; }
    internal IIdleTimeProvider? IdleTimeProvider { get; init; }
    internal bool SuppressApplicationShutdownOnClose { get; init; }
    internal Action? ShutdownApplication { get; init; }
    internal AmbientActionScheduler? AmbientScheduler { get; init; }
    internal IAutoStartService? AutoStartService { get; init; }
    internal Func<AgentMemorySnapshot, Task>? SaveAgentMemoryAsync { get; init; }
    internal Func<PetSettings, Task>? SaveSettingsAsync { get; init; }
    internal DialogueService? DialogueService { get; init; }
    internal TimeProvider? TimeProvider { get; init; }
    internal DialogueWarmupCoordinator? WarmupCoordinator { get; init; }
    internal Action<FrameworkElement>? AnnounceLiveRegionChanged { get; init; }
}
