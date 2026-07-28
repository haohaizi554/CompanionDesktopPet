using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using System.Windows;

namespace CompanionDesktopPet.UI;

/// <summary>
/// Defines the runtime collaborators used by <see cref="MainWindow"/> without
/// exposing test-only injection seams as an ever-growing constructor signature.
/// </summary>
internal sealed class MainWindowOptions
{
    internal MainWindowOptions(PetSettings settings, SettingsService settingsService)
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
    internal IForegroundFullscreenDetector? ForegroundFullscreenDetector { get; init; }
    internal DialogueScheduler? DialogueScheduler { get; init; }
    internal DialogueWarmupCoordinator? WarmupCoordinator { get; init; }
    internal Action<FrameworkElement>? AnnounceLiveRegionChanged { get; init; }
    internal IPetAnimationController? AnimationController { get; init; }
}

/// <summary>
/// Builds the single <see cref="MainWindowOptions"/> value used by the composition root.
/// Tests can construct options directly; production startup keeps its dependency wiring in one place.
/// </summary>
internal sealed class MainWindowOptionsBuilder
{
    private readonly PetSettings _settings;
    private readonly SettingsService _settingsService;

    internal MainWindowOptionsBuilder(PetSettings settings, SettingsService settingsService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService
            ?? throw new ArgumentNullException(nameof(settingsService));
    }

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
    internal IForegroundFullscreenDetector? ForegroundFullscreenDetector { get; init; }
    internal DialogueScheduler? DialogueScheduler { get; init; }
    internal DialogueWarmupCoordinator? WarmupCoordinator { get; init; }
    internal Action<FrameworkElement>? AnnounceLiveRegionChanged { get; init; }
    internal IPetAnimationController? AnimationController { get; init; }

    internal MainWindowOptions Build() => new(_settings, _settingsService)
    {
        AgentMemoryService = AgentMemoryService,
        AgentMemory = AgentMemory,
        IdleTimeProvider = IdleTimeProvider,
        SuppressApplicationShutdownOnClose = SuppressApplicationShutdownOnClose,
        ShutdownApplication = ShutdownApplication,
        AmbientScheduler = AmbientScheduler,
        AutoStartService = AutoStartService,
        SaveAgentMemoryAsync = SaveAgentMemoryAsync,
        SaveSettingsAsync = SaveSettingsAsync,
        DialogueService = DialogueService,
        TimeProvider = TimeProvider,
        ForegroundFullscreenDetector = ForegroundFullscreenDetector,
        DialogueScheduler = DialogueScheduler,
        WarmupCoordinator = WarmupCoordinator,
        AnnounceLiveRegionChanged = AnnounceLiveRegionChanged,
        AnimationController = AnimationController
    };
}
