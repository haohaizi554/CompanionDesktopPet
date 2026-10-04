using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet;

public partial class MainWindow : Window
{
    private readonly DialogueService _dialogue;
    private readonly Random _random = new();
    private readonly AutomaticDialogueCadenceController _automaticCadence;
    private readonly IForegroundFullscreenDetector _foregroundFullscreenDetector;
    private readonly FullscreenStateTracker _fullscreenState = new();
    private readonly SettingsService _settingsService;
    private readonly Func<AgentMemorySnapshot, Task>? _saveAgentMemoryAsync;
    private readonly Func<PetSettings, Task> _saveSettingsAsync;
    private readonly IPetAnimationController _animation;
    private readonly DispatcherTimer _automaticTimer = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private PetReminderStore? _reminders;
    private readonly DispatcherTimer _eventTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _ambientTimer = new();
    private readonly BubbleCountdownController _bubbleCountdown = new();
    private readonly PetActionCoordinator _actionCoordinator = new();
    private readonly SemaphoreSlim _memorySaveGate = new(1, 1);
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly AmbientActionScheduler _ambientScheduler;
    private readonly IIdleTimeProvider _idleTimeProvider;
    private readonly IAutoStartService _autoStartService;
    private readonly TimeProvider _timeProvider;
    private readonly DialogueWarmupCoordinator _dialogueWarmup;
    private readonly Action<FrameworkElement> _announceLiveRegionChanged;
    private readonly CancellationTokenSource _dialogueWarmupLifetime = new();
    private readonly bool _suppressApplicationShutdownOnClose;
    private readonly Action _shutdownApplication;
    private const double BubbleShadowSafety = 10;
    private const string DialogueWarmupFailureMessage = "文库没醒，点我重试";
    private CompanionEventPump? _eventPump;
    private PetSettings _settings;
    private PetScale _scale;
    private bool _paused;
    private bool _dragged;
    private bool _shutdownRequested;
    private StartupGreetingPhase _startupGreetingState = StartupGreetingPhase.Pending;
    private bool _runningSmokeProbe;
    private bool _isClosed;
    private bool _lastKnownAutoStart;
    private bool _trayAvailable;
    private bool _exitCommandRunning;
    private PetAmbientAction _pendingAmbientAction;
    private long _ambientScheduleGeneration;
    private long _armedAmbientGeneration;
    private long _ambientDueTimestamp;
    private const double ClickSlopDips = 16;
    private System.Windows.Point _mouseDown;
    private ScreenPoint _dragGrabOffset;
    private double _pressLeft;
    private double _pressTop;
    private double _lastDragLeft;
    private bool _dragCompletionStarted;
    private BubblePlacementSide _bubbleSide = BubblePlacementSide.Above;
    private bool _bubbleSuspendedForWindowHide;
    private Task<DialogueWarmupOutcome>? _observedDialogueWarmup;
    private (DialogueWarmupOutcome Outcome, long Generation)? _pendingDialogueWarmupOutcome;
    private long _appliedDialogueWarmupGeneration;
    private long _dialogueReplyRevision;
    private long _dialogueWarmupGeneration;
    private long _startupFallbackReplyRevision;
    private long _userRetryFallbackReplyRevision;
    private bool _replayStartupAfterWarmupRequested;
    private bool _replayUserClickAfterWarmupRequested;
    private DialogueWarmupViewState _dialogueWarmupViewState = DialogueWarmupViewState.Pending;
    private IInputElement? _controlMenuFocusReturnTarget;
    private bool _controlMenuOpenedFromKeyboard;
    private bool _controlMenuPlacementReady;
    private ControlMenuPlacement _controlMenuPlacement;
    private bool _corpusMenuBuilt;
    private readonly HashSet<MenuItem> _populatedCorpusMenus = [];
    private readonly DeveloperTestParameters _developerParameters = DeveloperTestParameters.CreateDefault();
    private readonly DialogueScheduler _dialogueScheduler;
    private DeveloperModeWindow? _developerWindow;
    private VoiceLibraryWindow? _voiceLibraryWindow;
    private bool _voiceLibraryMenuBuilt;
    private readonly IVoiceSpeaker? _voice;
    private PersonaDialogueClient? _personaDialogue;
    private readonly SpokenLineBridge _spokenBridge = new();
    private readonly DialogueSendQueue _dialogueQueue = new();
    private int _spokenFlush;
    private DialogueComposerWindow? _dialogueComposer;
    private int _personaDialogueSession;
    private int _dialoguePump;
    private readonly DispatcherTimer _voiceProgressTimer;
    private MediaPlayer? _voicePlayer;
    private byte[] _mouthLevels = [];
    private string? _playedWavPath;
    private bool _voiceLineActive;
    private string? _requestedVoiceText;
    private string? _lastUtterance;
    private bool _awaitingReply;
    private bool _voicePlaying;
    private VoiceClip? _deferredVoiceClip;
    private DateTime _voicePhaseStarted;
    private TimeSpan _playbackDuration = TimeSpan.FromSeconds(4);
    private string? _synthesizingText;
    private bool _isHiddenToTray;
    private FullscreenSnapshot _fullscreen;

    internal AgentReply? LastReply { get; private set; }

    internal MainWindowRuntimeSnapshot CaptureRuntimeState() =>
        new(
            _paused,
            _memoryTimer.IsEnabled,
            _automaticTimer.IsEnabled,
            _eventTimer.IsEnabled,
            _eventTimer.Interval,
            _ambientTimer.IsEnabled,
            _bubbleTimer.IsEnabled,
            _bubbleCountdown.State,
            _animation is AnimationController { IsSuspended: true },
            _actionCoordinator.State,
            _dialogueReplyRevision);

    private bool InteractionFrozen => _exitCommandRunning || _isClosed;
    private bool PresentationSuspended => InteractionFrozen || _isHiddenToTray;

    internal MainWindow(MainWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        InitializeComponent();
        _voiceProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _voiceProgressTimer.Tick += VoiceProgress_Tick;
        _settings = options.Settings;
        _settingsService = options.SettingsService;
        _saveAgentMemoryAsync = options.SaveAgentMemoryAsync
            ?? (options.AgentMemoryService is null
                ? null
                : options.AgentMemoryService.SaveAsync);
        _saveSettingsAsync = options.SaveSettingsAsync ?? _settingsService.SaveAsync;
        _idleTimeProvider = options.IdleTimeProvider ?? new WindowsIdleTimeProvider();
        _ambientScheduler = options.AmbientScheduler ?? new AmbientActionScheduler();
        _autoStartService = options.AutoStartService
            ?? (options.SuppressApplicationShutdownOnClose
                ? DisabledAutoStartService.Instance
                : new WindowsAutoStartService());
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
        _suppressApplicationShutdownOnClose = options.SuppressApplicationShutdownOnClose;
        _shutdownApplication = options.ShutdownApplication
            ?? (() => System.Windows.Application.Current?.Shutdown());
        _dialogue = options.DialogueService
            ?? DialogueService.CreateDeferred(
                options.AgentMemory,
                timeProvider: _timeProvider);
        _dialogueWarmup = options.WarmupCoordinator
            ?? new DialogueWarmupCoordinator(_dialogue, _timeProvider);
        _announceLiveRegionChanged = options.AnnounceLiveRegionChanged
            ?? RaiseLiveRegionChanged;
        _foregroundFullscreenDetector = options.ForegroundFullscreenDetector
            ?? new WindowsForegroundFullscreenDetector();
        _dialogueScheduler = options.DialogueScheduler ?? new DialogueScheduler();
        _automaticCadence = new AutomaticDialogueCadenceController(
            _dialogueScheduler,
            _timeProvider);
        _animation = options.AnimationController ?? new AnimationController(
            BreathingScale,
            SwayRotation,
            FloatingOffset,
            ReactionScale,
            ReactionRotation,
            ActionScale,
            ActionRotation,
            ActionOffset,
            [HeartOne, HeartTwo, HeartThree],
            BlinkOverlay,
            GreetingBadge,
            GreetingBadgeOffset);

        Loaded += Window_Loaded;
        ContentRendered += Window_ContentRendered;
        LocationChanged += Window_LocationChanged;
        Closing += (_, _) => _dialogueComposer?.CloseForShutdown();
        Closed += Window_Closed;
        PetImage.PreviewMouseLeftButtonDown += PetImage_MouseLeftButtonDown;
        PetImage.PreviewMouseMove += PetImage_MouseMove;
        PetImage.PreviewMouseLeftButtonUp += PetImage_MouseLeftButtonUp;
        PetImage.LostMouseCapture += PetImage_LostMouseCapture;
        PreviewKeyDown += CharacterStage_PreviewKeyDown;
        CharacterStage.MouseEnter += BubbleHover_MouseEnter;
        CharacterStage.MouseLeave += BubbleHover_MouseLeave;
        SpeechBubble.MouseEnter += BubbleHover_MouseEnter;
        SpeechBubble.MouseLeave += BubbleHover_MouseLeave;
        SayMenuItem.Click += SaySomething_Click;
        _voice = options.VoiceSpeaker;
        _personaDialogue = options.PersonaDialogue ?? PersonaDialogueClient.TryCreate();
        DialogueMenuItem.Click += ToggleDialogue_Click;
        DialogueEndpointMenuItem.Click += ConfigureDialogueEndpoint_Click;
        if (_voice is not null)
        {
            VoiceMenuItem.IsEnabled = true;
            VoiceMenuItem.IsChecked = _voice.Enabled;
            AutomationProperties.SetHelpText(VoiceMenuItem, "打开后佳怡会把这句话念出来。句子会留到声音结束。点她时，先说你点的这句。");
            _voice.SynthesisStarted += OnVoiceSynthesisStarted;
            _voice.PlaybackReady += OnVoicePlaybackReady;
            _voice.SynthesisContinuing += OnVoiceSynthesisContinuing;
            _voice.VoiceIdle += OnVoiceIdle;
        }

        VoiceMenuItem.Click += ToggleVoice_Click;
        GreetingMenuItem.Click += Greeting_Click;
        PauseMenuItem.Click += ToggleAnimation_Click;
        SmallSizeMenuItem.Click += SetSize_Click;
        NormalSizeMenuItem.Click += SetSize_Click;
        LargeSizeMenuItem.Click += SetSize_Click;
        TopmostMenuItem.Click += ToggleTopmost_Click;
        ControlMenu.CustomPopupPlacementCallback = PlaceControlMenu;
        ControlMenu.Opened += ControlMenu_Opened;
        ControlMenu.Closed += ControlMenu_Closed;
        ControlMenu.PreviewKeyDown += ControlMenu_PreviewKeyDown;
        AutoStartMenuItem.Click += ToggleAutoStart_Click;
        RestorePositionMenuItem.Click += RestorePosition_Click;
        HideToTrayMenuItem.Click += HideToTray_Click;
        ExitMenuItem.Click += Exit_Click;
        BrowseAllCorpusMenuItem.Click += BrowseAllCorpus_Click;
        CorpusMenuItem.SubmenuOpened += CorpusMenu_SubmenuOpened;
        BrowseVoiceLibraryMenuItem.Click += BrowseVoiceLibrary_Click;
        VoiceLibraryMenuItem.SubmenuOpened += VoiceLibraryMenu_SubmenuOpened;
        if (_settings.Tuning is { } tuning && _developerParameters.TryCopyTuning(tuning))
        {
            if (_voice is not null)
            {
                _voice.Enabled = tuning.VoiceEnabled;
                VoiceMenuItem.IsChecked = tuning.VoiceEnabled;
            }
        }

        WriteDeveloperDraft(_developerParameters);
        _voice?.ApplySpeech(
            _developerParameters.SpeechSpeed,
            _developerParameters.SpeechTemperature,
            _developerParameters.SpeechRepetition,
            _developerParameters.TopK,
            _developerParameters.TopP);
        UpdateTrayAvailabilityControls();
        _bubbleTimer.Tick += BubbleTimer_Tick;
        _automaticTimer.Tick += AutomaticTimer_Tick;
        _memoryTimer.Tick += MemoryTimer_Tick;
        _reminderTimer.Tick += ReminderTimer_Tick;
        _eventTimer.Tick += EventTimer_Tick;
        _ambientTimer.Tick += AmbientTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        _scale = _settings.Scale;
        _paused = _settings.AnimationPaused;
        Topmost = _settings.AlwaysOnTop;
        TopmostMenuItem.IsChecked = Topmost;
        DialogueMenuItem.IsChecked = _settings.DialogueEnabled;
        UpdateDialogueComposer();
        ApplyScale(_scale);
        PlaceOnScreen();
        _animation.StartIdle();
        if (_paused)
        {
            _animation.PauseIdle();
            _actionCoordinator.Pause();
        }

        UpdatePauseLabel();
        var now = LocalNow;
        var fullscreen = ObserveFullscreen();
        var startupDisplayed = ShowEventBubble(CompanionEvent.Startup, now, fullscreen);
        _startupFallbackReplyRevision = _dialogueReplyRevision;
        _eventPump = new CompanionEventPump(now, _idleTimeProvider.GetIdleTime());
        _eventTimer.Start();
        if (!startupDisplayed)
        {
            ArmAutomaticTimer(now, fullscreen);
        }

        if (DialogueMenuItem.IsChecked)
        {
            _ = EnsurePersonaDialogueAsync();
        }

        _reminderTimer.Start();
    }

    private void Window_ContentRendered(object? sender, EventArgs e) =>
        ProcessPresentationRendered();

    internal void ProcessPresentationRendered()
    {
        if (InteractionFrozen)
        {
            return;
        }

        ObserveDialogueWarmup(replayStartupWhenReady: true);
        ScheduleNextAmbientAction();
        if (_voice is JiayiVoiceSpeaker speaker)
        {
            speaker.Prepare();
        }
    }

    private void AmbientTimer_Tick(object? sender, EventArgs e) =>
        ProcessAmbientSchedule();

    internal void ProcessAmbientSchedule()
    {
        if (PresentationSuspended)
        {
            _ambientTimer.Stop();
            return;
        }

        if (_runningSmokeProbe
            || _actionCoordinator.State == PetActionState.Paused
            || _paused)
        {
            PreserveScheduledStartupGreeting();
            InvalidateAmbientSchedule();
            return;
        }

        if (_armedAmbientGeneration == 0)
        {
            _ambientTimer.Stop();
            return;
        }

        var remaining = _ambientScheduler.GetRemaining(_ambientDueTimestamp);
        if (_armedAmbientGeneration != _ambientScheduleGeneration
            || remaining > TimeSpan.Zero)
        {
            _ambientTimer.Stop();
            if (_armedAmbientGeneration == _ambientScheduleGeneration)
            {
                _ambientTimer.Interval = remaining > TimeSpan.FromMilliseconds(1)
                    ? remaining
                    : TimeSpan.FromMilliseconds(1);
                _ambientTimer.Start();
            }

            return;
        }

        var action = _pendingAmbientAction;
        var isStartupGreeting = action == PetAmbientAction.Greeting
            && _startupGreetingState == StartupGreetingPhase.Scheduled;
        InvalidateAmbientSchedule();
        if (!_actionCoordinator.TryBeginAmbient(action))
        {
            if (isStartupGreeting)
            {
                _startupGreetingState = StartupGreetingPhase.Pending;
            }

            return;
        }

        if (isStartupGreeting)
        {
            _startupGreetingState = StartupGreetingPhase.Running;
        }

        PlayAmbientAction(action, isStartupGreeting);
    }

    internal AmbientRuntimeSnapshot CaptureAmbientRuntime() =>
        new(
            _actionCoordinator.State,
            _startupGreetingState,
            _ambientTimer.IsEnabled,
            _ambientTimer.Interval,
            _pendingAmbientAction);

    private void PlayAmbientAction(PetAmbientAction action, bool isStartupGreeting = false)
    {
        if (action == PetAmbientAction.Blink)
        {
            _animation.PlayBlink(
                _ambientScheduler.ShouldDoubleBlink(),
                () => CompleteAmbientAction(PetActionState.Blinking));
            return;
        }

        _animation.PlayGreeting(isStartupGreeting
            ? CompleteStartupGreeting
            : () => CompleteAmbientAction(PetActionState.Greeting));
    }

    private void CompleteStartupGreeting()
    {
        if (_startupGreetingState == StartupGreetingPhase.Running)
        {
            _startupGreetingState = StartupGreetingPhase.Completed;
        }

        CompleteAmbientAction(PetActionState.Greeting);
    }

    private void CompleteAmbientAction(PetActionState completed)
    {
        _actionCoordinator.Complete(completed);
        if (_actionCoordinator.State == PetActionState.Idle)
        {
            ScheduleNextAmbientAction();
        }
    }

    private void ScheduleFreshBlink() =>
        ScheduleAmbientAction(PetAmbientAction.Blink, _ambientScheduler.NextBlinkDelay());

    private void ScheduleNextAmbientAction()
    {
        if (PresentationSuspended
            || _runningSmokeProbe
            || _paused
            || _actionCoordinator.State != PetActionState.Idle)
        {
            return;
        }

        if (_startupGreetingState == StartupGreetingPhase.Pending)
        {
            ScheduleAmbientAction(
                PetAmbientAction.Greeting,
                TimeSpan.FromMilliseconds(650));
            _startupGreetingState = StartupGreetingPhase.Scheduled;
            return;
        }

        if (_startupGreetingState == StartupGreetingPhase.Completed)
        {
            ScheduleFreshBlink();
        }
    }

    private void ScheduleAmbientAction(PetAmbientAction action, TimeSpan delay)
    {
        InvalidateAmbientSchedule();
        if (PresentationSuspended
            || _runningSmokeProbe
            || _paused
            || _actionCoordinator.State != PetActionState.Idle)
        {
            return;
        }

        _pendingAmbientAction = action;
        _ambientTimer.Interval = delay;
        _ambientDueTimestamp = _ambientScheduler.GetDeadline(delay);
        _armedAmbientGeneration = _ambientScheduleGeneration;
        _ambientTimer.Start();
    }

    private void PreserveScheduledStartupGreeting()
    {
        if (_startupGreetingState == StartupGreetingPhase.Scheduled)
        {
            _startupGreetingState = StartupGreetingPhase.Pending;
        }
    }

    private void InvalidateAmbientSchedule()
    {
        _ambientTimer.Stop();
        _ambientScheduleGeneration++;
        _armedAmbientGeneration = 0;
        _ambientDueTimestamp = 0;
    }

    public async Task<bool> RunSmokeActionProbeAsync()
    {
        if (InteractionFrozen)
        {
            return false;
        }

        PreserveScheduledStartupGreeting();
        InvalidateAmbientSchedule();
        _runningSmokeProbe = true;
        CancelActiveAmbientAction();
        var succeeded = false;
        try
        {
            if (_isClosed || _actionCoordinator.State != PetActionState.Idle)
            {
                return false;
            }

            var blinkCompleted = await RunSmokeActionAsync(PetAmbientAction.Blink);
            var greetingCompleted = blinkCompleted
                && await RunSmokeActionAsync(PetAmbientAction.Greeting);
            succeeded = greetingCompleted
                && _actionCoordinator.State == PetActionState.Idle;
        }
        catch (TimeoutException)
        {
            succeeded = false;
        }
        finally
        {
            InvalidateAmbientSchedule();
            CancelActiveAmbientAction();
            _runningSmokeProbe = false;
            ScheduleNextAmbientAction();
        }

        return succeeded && AmbientVisualsAreNeutral();
    }

    private async Task<bool> RunSmokeActionAsync(PetAmbientAction action)
    {
        if (!_actionCoordinator.TryBeginAmbient(action))
        {
            return false;
        }

        var expectedState = action == PetAmbientAction.Blink
            ? PetActionState.Blinking
            : PetActionState.Greeting;
        if (_actionCoordinator.State != expectedState)
        {
            return false;
        }

        var completed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (action == PetAmbientAction.Blink)
        {
            _animation.PlayBlink(
                doubleBlink: false,
                () =>
                {
                    _actionCoordinator.Complete(expectedState);
                    completed.TrySetResult(_actionCoordinator.State == PetActionState.Idle);
                });
        }
        else
        {
            _animation.PlayGreeting(() =>
            {
                _actionCoordinator.Complete(expectedState);
                completed.TrySetResult(_actionCoordinator.State == PetActionState.Idle);
            });
        }

        return await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private void CancelActiveAmbientAction()
    {
        if (_startupGreetingState == StartupGreetingPhase.Running)
        {
            _startupGreetingState = StartupGreetingPhase.Completed;
        }

        _animation.CancelAmbientAction();
        if (_actionCoordinator.State is PetActionState.Blinking or PetActionState.Greeting)
        {
            _actionCoordinator.Complete(_actionCoordinator.State);
        }
    }

    private bool AmbientVisualsAreNeutral() =>
        !BlinkOverlay.HasAnimatedProperties
        && !GreetingBadge.HasAnimatedProperties
        && !GreetingBadgeOffset.HasAnimatedProperties
        && !ActionScale.HasAnimatedProperties
        && !ActionRotation.HasAnimatedProperties
        && !ActionOffset.HasAnimatedProperties
        && BlinkOverlay.Opacity == 0
        && GreetingBadge.Opacity == 0
        && GreetingBadgeOffset.X == 0
        && GreetingBadgeOffset.Y == 8
        && ActionScale.ScaleX == 1
        && ActionScale.ScaleY == 1
        && ActionRotation.Angle == 0
        && ActionOffset.X == 0
        && ActionOffset.Y == 0;

    public bool TryVerifySmokeReadiness(out string failure)
    {
        if (!IsLoaded || !IsVisible)
        {
            failure = "The main window has not rendered.";
            return false;
        }

        if (PetImage.Source is null
            || !PetImage.IsVisible
            || PetImage.ActualWidth <= 0
            || PetImage.ActualHeight <= 0)
        {
            failure = "The pet image is not visible.";
            return false;
        }

        if (!_dialogue.IsReady)
        {
            failure = _dialogueWarmup.CanRetryAfterFailure
                || _dialogueWarmupViewState == DialogueWarmupViewState.RetryAvailable
                ? "The full dialogue runtime failed to warm up."
                : "The full dialogue runtime is not ready.";
            return false;
        }

        if (LastReply is not
            {
                Trigger: CompanionEvent.Startup,
                ShouldDisplayText: true,
                SourceLine.Enabled: true
            } reply)
        {
            failure = "The startup reply is not ready.";
            return false;
        }

        if (reply.SceneId.StartsWith("fallback:", StringComparison.Ordinal)
            || reply.SourceLine!.SourceKind == "builtin_fallback")
        {
            failure = "The full startup reply is not ready.";
            return false;
        }

        if (SpeechBubble.Visibility != Visibility.Visible
            || !SpeechBubble.IsVisible
            || string.IsNullOrWhiteSpace(SpeechText.Text)
            || !string.Equals(SpeechText.Text, reply.Text, StringComparison.Ordinal))
        {
            failure = "The startup reply is not visible in the speech bubble.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    internal async Task<bool> PrepareSmokeReadinessAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (InteractionFrozen)
        {
            return false;
        }

        ObserveDialogueWarmup(replayStartupWhenReady: false);
        var warmup = _observedDialogueWarmup
            ?? _dialogueWarmup.StartAsync(_dialogueWarmupLifetime.Token);
        DialogueWarmupOutcome outcome;
        try
        {
            outcome = await warmup.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (outcome != DialogueWarmupOutcome.Ready || InteractionFrozen)
        {
            return false;
        }

        if (!Dispatcher.CheckAccess())
        {
            return await Dispatcher.InvokeAsync(PrepareRealStartupForSmoke);
        }

        return PrepareRealStartupForSmoke();
    }

    private bool PrepareRealStartupForSmoke()
    {
        if (InteractionFrozen || !_dialogue.IsReady)
        {
            return false;
        }

        if (LastReply is not
            {
                Trigger: CompanionEvent.Startup,
                ShouldDisplayText: true,
                SourceLine.SourceKind: not "builtin_fallback"
            } reply
            || reply.SceneId.StartsWith("fallback:", StringComparison.Ordinal))
        {
            ShowEventBubble(CompanionEvent.Startup, LocalNow, ObserveFullscreen());
        }

        UpdateLayout();
        SpeechBubble.UpdateLayout();
        PositionBubble();
        return TryVerifySmokeReadiness(out _);
    }

    private void PlaceOnScreen()
    {
        var workAreas = WorkAreaService.GetWorkAreas();
        if (workAreas.Count == 0)
        {
            var work = SystemParameters.WorkArea;
            workAreas = [new ScreenRect(work.Left, work.Top, work.Width, work.Height)];
        }

        var localBounds = GetCharacterLocalBounds();
        var requested = double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top)
            ? DefaultPosition(workAreas[0], localBounds)
            : new ScreenPoint(_settings.Left, _settings.Top);
        var clamped = ScreenPlacementService.ClampVisibleBounds(
            requested,
            localBounds,
            workAreas);
        Left = clamped.X;
        Top = clamped.Y;
    }

    private static ScreenPoint DefaultPosition(ScreenRect workArea, ScreenRect localBounds) =>
        new(
            workArea.Right - localBounds.Right - 24,
            workArea.Bottom - localBounds.Bottom - 24);

    private void EnsureCurrentPositionIsVisible()
    {
        var workAreas = WorkAreaService.GetWorkAreas();
        if (workAreas.Count == 0 || !double.IsFinite(Left) || !double.IsFinite(Top))
        {
            return;
        }

        var clamped = ScreenPlacementService.ClampVisibleBounds(
            new ScreenPoint(Left, Top),
            GetCharacterLocalBounds(),
            workAreas);
        Left = clamped.X;
        Top = clamped.Y;
    }

    private ScreenRect GetCharacterLocalBounds()
    {
        var width = CharacterStage.Width;
        var height = CharacterStage.Height;
        return new ScreenRect(
            (ActualWidth - width) / 2,
            ActualHeight - height,
            width,
            height);
    }

    private ScreenRect GetCharacterScreenBounds()
    {
        var local = GetCharacterLocalBounds();
        return new ScreenRect(
            Left + local.Left,
            Top + local.Top,
            local.Width,
            local.Height);
    }

    private void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (InteractionFrozen)
        {
            e.Handled = true;
            return;
        }

        CharacterStage.Focus();
        _mouseDown = e.GetPosition(this);
        _pressLeft = Left;
        _pressTop = Top;
        var grab = e.GetPosition(CharacterStage);
        _dragGrabOffset = new ScreenPoint(grab.X, grab.Y);
        _dragged = false;
        _dragCompletionStarted = false;
        PetImage.CaptureMouse();
        e.Handled = true;
    }

    private void PetImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (InteractionFrozen || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (!_dragged
            && Math.Abs(current.X - _mouseDown.X) <= ClickSlopDips
            && Math.Abs(current.Y - _mouseDown.Y) <= ClickSlopDips)
        {
            return;
        }

        if (!_dragged)
        {
            BeginDragGesture();
        }

        var workAreas = WorkAreaService.GetWorkAreas();
        if (workAreas.Count == 0)
        {
            return;
        }

        var target = ScreenPlacementService.PlaceGrabbedVisibleBounds(
            new ScreenPoint(Left + current.X, Top + current.Y),
            _dragGrabOffset,
            GetCharacterLocalBounds(),
            workAreas);
        Left = target.X;
        Top = target.Y;
    }

    internal void BeginDragGesture()
    {
        _dragged = true;
        _dragCompletionStarted = false;
        BeginDragAction();
        _lastDragLeft = Left;
    }

    internal async Task CompleteDragAfterMoveAsync()
    {
        if (InteractionFrozen)
        {
            return;
        }

        await SaveSettingsAsync(skipWhenExiting: true);
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_dragged)
        {
            var horizontalDelta = Left - _lastDragLeft;
            _lastDragLeft = Left;
            _animation.SetDragLean(horizontalDelta);
        }

        PositionBubble();
        PositionDialogue();
    }

    private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!InteractionFrozen && _dragged && PointerRepositioned())
        {
            FinishDragOnce();
        }
        else if (!InteractionFrozen)
        {
            AbandonDragGesture();
            var clickPosition = e.GetPosition(PetImage);
            ReactAndSpeak(ResolveClickSide(clickPosition.X, PetImage.ActualWidth));
        }

        _dragged = false;
        PetImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PetImage_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (PointerRepositioned())
        {
            FinishDragOnce();
            return;
        }

        AbandonDragGesture();
    }

    private bool PointerRepositioned() =>
        Math.Abs(Left - _pressLeft) > ClickSlopDips
        || Math.Abs(Top - _pressTop) > ClickSlopDips;

    private void AbandonDragGesture()
    {
        if (!_dragged)
        {
            return;
        }

        _dragged = false;
        _dragCompletionStarted = true;
        _actionCoordinator.CancelDrag();
        _animation.SetDragLean(0);
    }

    internal void FinishDragOnce()
    {
        if (!_dragged || _dragCompletionStarted)
        {
            return;
        }

        _dragCompletionStarted = true;
        _dragged = false;
        BeginLandingAction();
        _ = CompleteDragAfterMoveAsync();
    }

    internal static ClickSide ResolveClickSide(double horizontalPosition, double renderedWidth)
    {
        if (!double.IsFinite(horizontalPosition)
            || !double.IsFinite(renderedWidth)
            || renderedWidth <= 0)
        {
            return ClickSide.Left;
        }

        return horizontalPosition < renderedWidth / 2
            ? ClickSide.Left
            : ClickSide.Right;
    }

    private void ReactAndSpeak(ClickSide? clickSide = null)
    {
        if (InteractionFrozen)
        {
            return;
        }

        var retryFailedWarmup = _dialogueWarmupViewState is
            DialogueWarmupViewState.RetryAvailable or DialogueWarmupViewState.Retrying;

        if (clickSide is { } resolvedClickSide)
        {
            _animation.PlayClickReaction(resolvedClickSide);
        }
        else
        {
            _animation.PlayClickReaction();
        }

        ShowEventBubble(CompanionEvent.Click, LocalNow, ObserveFullscreen());
        if (retryFailedWarmup)
        {
            RetryDialogueWarmupAfterUserAction();
        }

    }

    internal void BeginDragAction()
    {
        if (InteractionFrozen)
        {
            return;
        }

        PreserveScheduledStartupGreeting();
        InvalidateAmbientSchedule();
        CancelActiveAmbientAction();
        _actionCoordinator.BeginDrag();
    }

    internal void BeginLandingAction()
    {
        if (InteractionFrozen)
        {
            return;
        }

        _actionCoordinator.BeginLanding();
        _animation.PlayLanding(() =>
        {
            _actionCoordinator.Complete(PetActionState.Landing);
            if (!InteractionFrozen && _actionCoordinator.State == PetActionState.Idle)
            {
                ScheduleNextAmbientAction();
            }
        });
    }

    private void RememberUtterance(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "……")
        {
            return;
        }

        _lastUtterance = text.Trim();
    }

    internal void ShowBubble(string text, bool speak = false, string? tone = null, string? trigger = null, bool urgent = false)
    {
        if (InteractionFrozen)
        {
            return;
        }

        var waiting = text == "……";
        if (!waiting)
        {
            _awaitingReply = false;
        }

        var incoming = text;
        text = SpeechFragments.DropLeadingRepeat(text, _lastUtterance);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (waiting || string.IsNullOrWhiteSpace(incoming))
            {
                return;
            }

            text = "我换一句。";
        }

        if (speak
            && _voice is { Enabled: true }
            && _voiceLineActive
            && string.Equals(_lastUtterance, FallbackDialogueCatalog.StartupLine.Text, StringComparison.Ordinal))
        {
            StopVoicePlayback();
        }

        if (!speak && _voiceLineActive)
        {
            StopVoicePlayback();
        }

        if (waiting)
        {
            _awaitingReply = true;
        }

        RememberUtterance(text);

        if (speak && _voice is { Enabled: true })
        {
            _requestedVoiceText = text;
            _voice.Speak(text, tone, trigger, urgent);
            if (_isHiddenToTray)
            {
                if (!_voiceLineActive || urgent)
                {
                    SpeechText.Text = text;
                    AutomationProperties.SetName(SpeechText, $"佳怡说：{text}");
                }

                _bubbleSuspendedForWindowHide = true;
                BubblePopup.IsOpen = false;
                _bubbleTimer.Stop();
                return;
            }

            if (_voiceLineActive && !urgent)
            {
                return;
            }

            if (_voicePlaying)
            {
                ReleaseVoicePlayer();
                _voicePlaying = false;
            }

            SpeechText.Text = text;
            AutomationProperties.SetName(SpeechText, $"佳怡说：{text}");
            SpeechBubble.Visibility = Visibility.Visible;
            OpenSpeechPopup();
            _bubbleCountdown.Show();
            _bubbleCountdown.Suspend();
            _bubbleTimer.Stop();
            _voiceLineActive = true;
            BeginVoiceWait(text);
            return;
        }

        SpeechText.Text = text;
        AutomationProperties.SetName(SpeechText, $"佳怡说：{text}");
        SpeechBubble.Visibility = Visibility.Visible;

        _bubbleCountdown.Show();
        if (waiting)
        {
            _bubbleCountdown.Suspend();
            _bubbleTimer.Stop();
        }

        if (_isHiddenToTray)
        {
            _bubbleSuspendedForWindowHide = true;
            BubblePopup.IsOpen = false;
            _bubbleTimer.Stop();
            return;
        }

        OpenSpeechPopup();
        _announceLiveRegionChanged(SpeechText);
        if (!waiting)
        {
            SynchronizeBubbleTimer();
        }
    }

    private void OpenSpeechPopup()
    {
        BubblePopup.IsOpen = true;
        _bubbleSuspendedForWindowHide = false;
        SpeechBubble.UpdateLayout();
        PositionBubble();
        Dispatcher.BeginInvoke(PositionBubble, DispatcherPriority.Loaded);
        _announceLiveRegionChanged(SpeechText);
    }

    private void ToggleVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_voice is null)
        {
            VoiceMenuItem.IsChecked = false;
            return;
        }

        _voice.Enabled = VoiceMenuItem.IsChecked;
        UpdateVoiceParameterPanel();
        if (!_voice.Enabled)
        {
            StopVoicePlayback();
            if (_voice is JiayiVoiceSpeaker speaker)
            {
                speaker.ParkWhenQuiet();
            }
        }
    }

    private void ToggleDialogue_Click(object sender, RoutedEventArgs e)
    {
        UpdateDialogueComposer();
        if (DialogueMenuItem.IsChecked)
        {
            _ = EnsurePersonaDialogueAsync();
        }
        else
        {
            _personaDialogueSession++;
            _personaDialogue?.Stop();
            _dialogueQueue.Clear();
        }

        _ = SaveSettingsAsync(skipWhenExiting: true);
    }

    private DialogueComposerWindow Composer => _dialogueComposer ??= CreateComposer();

    private DialogueComposerWindow CreateComposer()
    {
        var composer = new DialogueComposerWindow { Owner = this, Topmost = Topmost };
        composer.SendRequested += (_, _) => SubmitDialogue();
        return composer;
    }

    private int _dialoguePosition;

    private void UpdateDialogueComposer()
    {
        var open = DialogueMenuItem.IsChecked && !_isHiddenToTray;
        if (!open)
        {
            _dialogueComposer?.Hide();
            return;
        }

        var composer = Composer;
        composer.Topmost = Topmost;
        if (!composer.IsVisible)
        {
            Dispatcher.BeginInvoke(OpenDialogueComposer, DispatcherPriority.ApplicationIdle);
            return;
        }

        PositionDialogue();
    }

    private void OpenDialogueComposer()
    {
        if (_dialogueComposer is not { } composer || composer.IsVisible || !DialogueMenuItem.IsChecked || _isHiddenToTray)
        {
            return;
        }

        composer.Topmost = Topmost;
        composer.ApplySide(DialoguePlacementSide.Below);
        PositionDialogue();
        composer.Show();
        composer.FocusInput();
    }

    private void PositionDialogue()
    {
        if (_dialoguePosition > 0
            || _dialogueComposer is not { } composer
            || !DialogueMenuItem.IsChecked
            || _isHiddenToTray)
        {
            return;
        }

        _dialoguePosition++;
        try
        {
            var size = composer.MeasureSurface();
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            var workAreas = WorkAreaService.GetWorkAreas();
            if (workAreas.Count == 0)
            {
                return;
            }

            var character = GetCharacterScreenBounds();
            var center = new ScreenPoint(
                character.Left + (character.Width / 2),
                character.Top + (character.Height / 2));
            var workArea = workAreas.FirstOrDefault(area => area.Contains(center));
            if (workArea.Width <= 0)
            {
                workArea = workAreas[0];
            }

            var safeWorkArea = new ScreenRect(
                workArea.Left + BubbleShadowSafety,
                workArea.Top + BubbleShadowSafety,
                Math.Max(0, workArea.Width - (BubbleShadowSafety * 2)),
                Math.Max(0, workArea.Height - (BubbleShadowSafety * 2)));
            var placement = DialoguePlacementService.Place(
                character,
                new ScreenSize(size.Width, size.Height),
                safeWorkArea);
            composer.ApplySide(placement.Side);
            var left = placement.Origin.X - BubbleShadowSafety;
            var top = placement.Origin.Y - BubbleShadowSafety;
            if (double.IsNaN(composer.Left) || Math.Abs(composer.Left - left) > 0.5)
            {
                composer.Left = left;
            }

            if (double.IsNaN(composer.Top) || Math.Abs(composer.Top - top) > 0.5)
            {
                composer.Top = top;
            }
        }
        finally
        {
            _dialoguePosition--;
        }
    }

    private void SubmitDialogue()
    {
        if (_dialogueComposer is null)
        {
            return;
        }

        if (InteractionFrozen || _personaDialogue is not { IsReady: true })
        {
            ShowBubble("我还在醒，等一下再说。");
            return;
        }

        var text = _dialogueComposer.Draft;
        if (!_dialogueQueue.TryEnqueue(text))
        {
            return;
        }

        _dialogueComposer.ClearDraft();
        StartDialoguePump();
    }

    private void StartDialoguePump()
    {
        if (Interlocked.CompareExchange(ref _dialoguePump, 1, 0) != 0)
        {
            return;
        }

        _ = PumpDialogueAsync();
    }

    private async Task PumpDialogueAsync()
    {
        try
        {
            while (_dialogueQueue.TryDequeue(out var text))
            {
                if (InteractionFrozen || !DialogueMenuItem.IsChecked)
                {
                    _awaitingReply = false;
                    _dialogueQueue.Clear();
                    break;
                }

                if (_personaDialogue is not { IsReady: true })
                {
                    _dialogueQueue.Clear();
                    ShowBubble("对话这会儿没接上，你再说一次。");
                    break;
                }

                ShowBubble("……");
                PersonaDialogueReply reply;
                try
                {
                    reply = await _personaDialogue.ReplyAsync(
                        text,
                        DialogueSkills.Describe(
                            _developerParameters,
                            _scale,
                            Topmost,
                            _paused,
                            _voice is { Enabled: true }));
                }
                catch (Exception exception) when (!IsFatalException(exception))
                {
                    Trace.TraceError("Dialogue reply failed: {0}", exception);
                    await RunOnUiAsync(() => ShowBubble("这句话我没接住，你再说一次。"));
                    continue;
                }

                var keepReading = true;
                await RunOnUiAsync(() => keepReading = DeliverDialogueReply(reply));
                if (!keepReading)
                {
                    break;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _dialoguePump, 0);
            if (_dialogueQueue.Count > 0)
            {
                StartDialoguePump();
            }
        }
    }

    private bool DeliverDialogueReply(PersonaDialogueReply reply)
    {
        if (InteractionFrozen || !DialogueMenuItem.IsChecked)
        {
            _dialogueQueue.Clear();
            CollapseBubble();
            return false;
        }

        ApplyDialogueActions(reply.Actions);
        var answer = reply.Ok && !string.IsNullOrWhiteSpace(reply.Text)
            ? reply.Text
            : "这句话我没接住，你再说一次。";
        var speak = reply.Ok && _voice is { Enabled: true };
        ShowBubble(answer, speak: speak, tone: "gentle", urgent: true);
        return true;
    }

    private Task RunOnUiAsync(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(action).Task;
    }

    private void ConfigureDialogueEndpoint_Click(object sender, RoutedEventArgs e)
    {
        if (!PersonaDialoguePaths.RuntimeInstalled())
        {
            ShowBubble("对话运行时还没放好。");
            return;
        }

        if (!OfferDialogueEndpoint())
        {
            return;
        }

        ReplaceDialogueClient();
        DialogueMenuItem.IsChecked = true;
        UpdateDialogueComposer();
        _ = EnsurePersonaDialogueAsync();
        _ = SaveSettingsAsync(skipWhenExiting: true);
    }

    private bool OfferDialogueEndpoint()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(OfferDialogueEndpoint);
        }

        var window = new DialogueEndpointWindow { Owner = this, Topmost = Topmost };
        return window.ShowDialog() == true;
    }

    private void ReplaceDialogueClient()
    {
        _personaDialogueSession++;
        _personaDialogue?.Stop();
        _personaDialogue?.Dispose();
        _personaDialogue = PersonaDialogueClient.TryCreate();
    }

    private async Task EnsurePersonaDialogueAsync()
    {
        var session = ++_personaDialogueSession;
        if (_personaDialogue is null)
        {
            if (!PersonaDialoguePaths.RuntimeInstalled())
            {
                DialogueMenuItem.IsChecked = false;
                UpdateDialogueComposer();
                ShowBubble("对话运行时还没放好。");
                _ = SaveSettingsAsync(skipWhenExiting: true);
                return;
            }

            if (PersonaDialoguePaths.ExistingConfigPath() is null && !OfferDialogueEndpoint())
            {
                DialogueMenuItem.IsChecked = false;
                UpdateDialogueComposer();
                _ = SaveSettingsAsync(skipWhenExiting: true);
                return;
            }

            _personaDialogue = PersonaDialogueClient.TryCreate();
            if (_personaDialogue is null)
            {
                DialogueMenuItem.IsChecked = false;
                UpdateDialogueComposer();
                ShowBubble("对话运行时还没放好。");
                _ = SaveSettingsAsync(skipWhenExiting: true);
                return;
            }
        }

        if (_personaDialogue.IsReady)
        {
            _ = FlushSpokenBridgeAsync();
            return;
        }

        var started = await _personaDialogue.StartAsync();
        if (session != _personaDialogueSession || !DialogueMenuItem.IsChecked)
        {
            return;
        }

        if (!started)
        {
            DialogueMenuItem.IsChecked = false;
            UpdateDialogueComposer();
            ShowBubble(string.IsNullOrWhiteSpace(_personaDialogue.StartupError)
                ? "对话暂时没连上。"
                : "对话暂时没连上。");
            _ = SaveSettingsAsync(skipWhenExiting: true);
            return;
        }

        _ = FlushSpokenBridgeAsync();
    }

    private void NoteCorpusLine(string text)
    {
        _spokenBridge.Note(text);
        if (_personaDialogue is { IsReady: true } && DialogueMenuItem.IsChecked)
        {
            _ = FlushSpokenBridgeAsync();
        }
    }

    private async Task FlushSpokenBridgeAsync()
    {
        if (Interlocked.CompareExchange(ref _spokenFlush, 1, 0) != 0)
        {
            return;
        }

        var failed = false;
        try
        {
            var client = _personaDialogue;
            if (client is not { IsReady: true } || !DialogueMenuItem.IsChecked)
            {
                return;
            }

            var batch = _spokenBridge.Take();
            for (var index = 0; index < batch.Count; index++)
            {
                if (client is not { IsReady: true } || !DialogueMenuItem.IsChecked)
                {
                    _spokenBridge.RestoreFront(batch.Skip(index).ToArray());
                    failed = true;
                    return;
                }

                if (!await client.RememberAsync(batch[index]))
                {
                    _spokenBridge.RestoreFront(batch.Skip(index).ToArray());
                    failed = true;
                    return;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _spokenFlush, 0);
            if (!failed
                && _spokenBridge.HasPending
                && _personaDialogue is { IsReady: true }
                && DialogueMenuItem.IsChecked)
            {
                _ = FlushSpokenBridgeAsync();
            }
        }
    }

    private void OnVoiceSynthesisStarted(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnVoiceSynthesisStarted(text));
            return;
        }

        if (!SpeechFragments.BelongsTo(text, _requestedVoiceText))
        {
            return;
        }

        if (_requestedVoiceText is not null
            && SpeechFragments.SplitForSpeech(_requestedVoiceText).Count > 1)
        {
            return;
        }

        _synthesizingText = text;
        if (_voicePlaying || InteractionFrozen || _isHiddenToTray || _voice is not { Enabled: true })
        {
            return;
        }

        SpeechText.Text = text;
        AutomationProperties.SetName(SpeechText, $"佳怡说：{text}");
        BeginVoiceWait(text);
    }

    private void OnVoiceSynthesisContinuing()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnVoiceSynthesisContinuing);
            return;
        }

        _voicePlaying = false;
        ReleaseVoicePlayer();
        if (!string.IsNullOrWhiteSpace(_synthesizingText))
        {
            SpeechText.Text = _synthesizingText;
            AutomationProperties.SetName(SpeechText, $"佳怡说：{_synthesizingText}");
        }

        BeginVoiceWait(_synthesizingText ?? SpeechText.Text);
    }

    private void OnVoiceIdle()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnVoiceIdle);
            return;
        }

        if (_awaitingReply || _voice is { HasPendingSpeech: true } || !_voiceLineActive)
        {
            return;
        }

        FinishVoiceHold(linger: true);
    }

    private void OnVoicePlaybackReady(VoiceClip clip)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnVoicePlaybackReady(clip));
            return;
        }

        if (InteractionFrozen || _voice is not { Enabled: true })
        {
            _deferredVoiceClip = null;
            StopVoicePlayback();
            return;
        }

        if (!SpeechFragments.BelongsTo(clip.Text, _requestedVoiceText))
        {
            _deferredVoiceClip = null;
            _voice?.NotifyPlaybackCompleted();
            return;
        }

        if (_isHiddenToTray)
        {
            _deferredVoiceClip = clip;
            return;
        }

        var wholeReply = _requestedVoiceText is not null
            && SpeechFragments.SplitForSpeech(_requestedVoiceText).Count > 1;
        if (!wholeReply)
        {
            SpeechText.Text = clip.Text;
            AutomationProperties.SetName(SpeechText, $"佳怡说：{clip.Text}");
        }
        if (!_voiceLineActive)
        {
            SpeechBubble.Visibility = Visibility.Visible;
            OpenSpeechPopup();
            _bubbleCountdown.Show();
            _bubbleCountdown.Suspend();
            _voiceLineActive = true;
        }

        try
        {
            ReleaseVoicePlayer();
            _mouthLevels = SpeechMouthTimeline.FromWav(clip.Path);
            _playedWavPath = clip.Path;
            var player = new MediaPlayer { Volume = 1 };
            player.MediaEnded += VoicePlayback_Ended;
            player.Open(new Uri(clip.Path, UriKind.Absolute));
            player.Play();
            _voicePlayer = player;
            _animation.SetSpeechMotion(true);
            _voicePlaying = true;
            _playbackDuration = ReadWavDuration(clip.Path);
            _voicePhaseStarted = DateTime.UtcNow;
            VoiceProgressRing.Visibility = Visibility.Visible;
            _voiceProgressTimer.Start();
        }
        catch (Exception)
        {
            ReleaseVoicePlayer();
            _voicePlaying = false;
            _voice?.NotifyPlaybackCompleted();
        }
    }

    private void VoiceProgress_Tick(object? sender, EventArgs e)
    {
        if (!_voiceLineActive)
        {
            _voiceProgressTimer.Stop();
            return;
        }

        if (_voicePlaying)
        {
            var position = _voicePlayer?.Position ?? TimeSpan.Zero;
            ShowMouth(SpeechMouthTimeline.LevelAt(_mouthLevels, position));
            var elapsed = position > TimeSpan.Zero
                ? position
                : DateTime.UtcNow - _voicePhaseStarted;
            var fraction = elapsed.TotalMilliseconds / Math.Max(1, _playbackDuration.TotalMilliseconds);
            SetVoiceProgress(fraction);
            if (position >= _playbackDuration
                || elapsed > _playbackDuration + TimeSpan.FromMilliseconds(400))
            {
                CompleteVoicePlayback();
            }

            return;
        }

        var spin = ((DateTime.UtcNow - _voicePhaseStarted).TotalMilliseconds / 8d) % 360d;
        SetVoiceSpinner(spin);
    }

    private void BeginVoiceWait(string text)
    {
        _voicePlaying = false;
        _voicePhaseStarted = DateTime.UtcNow;
        _synthesizingText = text;
        VoiceProgressRing.Visibility = Visibility.Visible;
        SetVoiceSpinner(0);
        _voiceProgressTimer.Start();
    }

    private void VoicePlayback_Ended(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(CompleteVoicePlayback);
            return;
        }

        CompleteVoicePlayback();
    }

    private void CompleteVoicePlayback()
    {
        if (!_voicePlaying)
        {
            return;
        }

        _voicePlaying = false;
        ReleaseVoicePlayer();
        _voice?.NotifyPlaybackCompleted();
    }

    private void ReleaseVoicePlayer()
    {
        if (_voicePlayer is not null)
        {
            _voicePlayer.MediaEnded -= VoicePlayback_Ended;
            _voicePlayer.Stop();
            _voicePlayer.Close();
            _voicePlayer = null;
        }

        HideMouth();
        _animation.SetSpeechMotion(false);
        if (_playedWavPath is null)
        {
            return;
        }

        var path = _playedWavPath;
        _playedWavPath = null;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ShowMouth(byte level)
    {
        MouthMid.Opacity = level == 1 ? 1 : 0;
        MouthOpen.Opacity = level == 2 ? 1 : 0;
    }

    private void HideMouth()
    {
        _mouthLevels = [];
        MouthMid.Opacity = 0;
        MouthOpen.Opacity = 0;
    }

    private void FinishVoiceHold(bool linger)
    {
        _voiceLineActive = false;
        _voicePlaying = false;
        _voiceProgressTimer.Stop();
        VoiceProgressRing.Visibility = Visibility.Collapsed;
        if (!linger || _bubbleCountdown.State == BubbleCountdownState.Hidden)
        {
            HideBubble();
            return;
        }

        _bubbleCountdown.Resume();
        if (_bubbleCountdown.State == BubbleCountdownState.Hidden)
        {
            CollapseBubble();
            return;
        }

        SynchronizeBubbleTimer();
    }

    private void SetVoiceProgress(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var units = VoiceProgressUnits();
        VoiceProgressRotate.Angle = -90;
        VoiceProgressArc.StrokeDashArray = new DoubleCollection { units * fraction, units };
    }

    private void SetVoiceSpinner(double angle)
    {
        var units = VoiceProgressUnits();
        VoiceProgressRotate.Angle = angle;
        VoiceProgressArc.StrokeDashArray = new DoubleCollection { units * 0.22, units };
    }

    private static double VoiceProgressUnits()
    {
        const double thickness = 3;
        const double diameter = 34;
        return Math.PI * (diameter - thickness) / thickness;
    }

    private static TimeSpan ReadWavDuration(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 44)
        {
            return TimeSpan.FromSeconds(4);
        }

        reader.ReadBytes(4);
        reader.ReadInt32();
        reader.ReadBytes(4);
        short channels = 1;
        var sampleRate = 32000;
        short bits = 16;
        var dataLength = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                var unread = size - 16;
                if (unread > 0)
                {
                    reader.ReadBytes(unread);
                }
            }
            else if (id == "data")
            {
                dataLength = size;
                break;
            }
            else if (size > 0)
            {
                reader.ReadBytes(size);
            }
        }

        var bytesPerSecond = sampleRate * Math.Max(1, (int)channels) * Math.Max(1, bits / 8);
        if (bytesPerSecond <= 0 || dataLength <= 0)
        {
            return TimeSpan.FromSeconds(4);
        }

        return TimeSpan.FromSeconds(dataLength / (double)bytesPerSecond);
    }

    private void StopVoicePlayback()
    {
        _deferredVoiceClip = null;
        _voice?.Stop();
        ReleaseVoicePlayer();
        if (_voiceLineActive)
        {
            FinishVoiceHold(linger: false);
        }
    }

    private void ShowLocalFeedbackWhenVisible(string text)
    {
        if (PresentationSuspended)
        {
            return;
        }

        var localTime = LocalNow;
        var fullscreen = ObserveFullscreen();
        ShowBubble(text);
        ArmAutomaticTimer(localTime, fullscreen);
    }

    internal void BubbleHover_MouseEnter(object sender, MouseEventArgs? e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        _bubbleCountdown.Enter(sender == SpeechBubble
            ? BubbleHoverTarget.Bubble
            : BubbleHoverTarget.Character);
        SynchronizeBubbleTimer();
    }

    internal void BubbleHover_MouseLeave(object sender, MouseEventArgs? e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        _bubbleCountdown.Leave(sender == SpeechBubble
            ? BubbleHoverTarget.Bubble
            : BubbleHoverTarget.Character);
        SynchronizeBubbleTimer();
    }

    internal void BubbleTimer_Tick(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
        {
            _bubbleTimer.Stop();
            return;
        }

        if (_awaitingReply || _voiceLineActive)
        {
            _bubbleTimer.Stop();
            return;
        }

        if (_bubbleCountdown.TryExpire())
        {
            CollapseBubble();
            return;
        }

        SynchronizeBubbleTimer();
    }

    private void HideBubble()
    {
        _bubbleCountdown.Hide();
        CollapseBubble();
    }

    private void CollapseBubble()
    {
        _awaitingReply = false;
        _bubbleTimer.Stop();
        _voiceProgressTimer.Stop();
        VoiceProgressRing.Visibility = Visibility.Collapsed;
        _voiceLineActive = false;
        _voicePlaying = false;
        SpeechText.Text = string.Empty;
        AutomationProperties.SetName(SpeechText, string.Empty);
        SpeechBubble.Visibility = Visibility.Collapsed;
        BubblePopup.IsOpen = false;
        _bubbleSuspendedForWindowHide = false;
        PositionDialogue();
    }

    private void PositionBubble()
    {
        if (!BubblePopup.IsOpen || SpeechBubble.Visibility != Visibility.Visible)
        {
            return;
        }

        var width = SpeechBubble.ActualWidth > 0
            ? SpeechBubble.ActualWidth
            : SpeechBubble.DesiredSize.Width;
        var height = SpeechBubble.ActualHeight > 0
            ? SpeechBubble.ActualHeight
            : SpeechBubble.DesiredSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var workAreas = WorkAreaService.GetWorkAreas();
        if (workAreas.Count == 0)
        {
            return;
        }

        var character = GetCharacterScreenBounds();
        var center = new ScreenPoint(
            character.Left + (character.Width / 2),
            character.Top + (character.Height / 2));
        var workArea = workAreas.FirstOrDefault(area => area.Contains(center));
        if (workArea.Width <= 0)
        {
            workArea = workAreas[0];
        }

        var safeWorkArea = new ScreenRect(
            workArea.Left + BubbleShadowSafety,
            workArea.Top + BubbleShadowSafety,
            Math.Max(0, workArea.Width - (BubbleShadowSafety * 2)),
            Math.Max(0, workArea.Height - (BubbleShadowSafety * 2)));
        var placement = BubblePlacementService.Place(
            character,
            new ScreenSize(width, height),
            safeWorkArea,
            _bubbleSide);
        _bubbleSide = placement.Side;
        BubbleArrowUp.Visibility = placement.Side == BubblePlacementSide.Below
            ? Visibility.Visible
            : Visibility.Collapsed;
        BubbleArrowDown.Visibility = placement.Side == BubblePlacementSide.Above
            ? Visibility.Visible
            : Visibility.Collapsed;
        var arrowMargin = new Thickness(
            Math.Max(0, placement.ArrowCenterX - (BubbleArrowDown.Width / 2)),
            0,
            0,
            0);
        BubbleArrowUp.Margin = arrowMargin;
        BubbleArrowDown.Margin = arrowMargin;
        // Popup does not reliably move its native HWND when a RelativePoint target's
        // parent window moves and the relative offsets remain unchanged. Absolute
        // coordinates change with the character and keep the bubble physically anchored.
        BubblePopup.HorizontalOffset = placement.Origin.X - BubbleShadowSafety;
        BubblePopup.VerticalOffset = placement.Origin.Y - BubbleShadowSafety;
        PositionDialogue();
    }

    internal void SynchronizeBubbleTimer()
    {
        _bubbleTimer.Stop();
        if (PresentationSuspended)
        {
            return;
        }

        if (_bubbleCountdown.State != BubbleCountdownState.CountingDown)
        {
            return;
        }

        var remaining = _bubbleCountdown.Remaining;
        _bubbleTimer.Interval = remaining > TimeSpan.FromMilliseconds(1)
            ? remaining
            : TimeSpan.FromMilliseconds(1);
        _bubbleTimer.Start();
    }

    private FullscreenSnapshot ObserveFullscreen()
    {
        bool? observed;
        try
        {
            observed = _foregroundFullscreenDetector.Observe(
                new WindowInteropHelper(this).Handle);
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            Trace.TraceError("Fullscreen observation failed: {0}", exception);
            observed = null;
        }

        _fullscreen = _fullscreenState.Update(observed);
        return _fullscreen;
    }

    private void ArmAutomaticTimer(DateTime localTime, FullscreenSnapshot fullscreen)
    {
        _automaticTimer.Stop();
        if (PresentationSuspended)
        {
            _automaticCadence.Reset();
            return;
        }

        var delay = _automaticCadence.Arm(localTime, fullscreen.EffectiveQuietMode);
        _automaticTimer.Interval = delay < TimeSpan.FromMilliseconds(1)
            ? TimeSpan.FromMilliseconds(1)
            : delay;
        _automaticTimer.Start();
    }

    private void DisarmAutomaticTimer()
    {
        _automaticTimer.Stop();
        _automaticCadence.Reset();
    }

    internal AutomaticDialogueRuntimeSnapshot CaptureAutomaticDialogueRuntime()
    {
        var cadence = _automaticCadence.Capture();
        return new AutomaticDialogueRuntimeSnapshot(
            cadence.IsArmed && _automaticTimer.IsEnabled,
            cadence.Delay,
            cadence.Mode,
            cadence.ArmedAtTimestamp,
            _fullscreen);
    }

    private void AutomaticTimer_Tick(object? sender, EventArgs e) => ProcessAutomaticTimerTick();

    internal void ProcessAutomaticTimerTick()
    {
        _automaticTimer.Stop();
        if (PresentationSuspended)
        {
            DisarmAutomaticTimer();
            return;
        }

        var now = LocalNow;
        var fullscreen = ObserveFullscreen();
        var evaluation = _automaticCadence.Evaluate(
            now,
            fullscreen.EffectiveQuietMode);
        switch (evaluation.Decision)
        {
            case AutomaticCadenceDecision.Wait:
                _automaticTimer.Stop();
                _automaticTimer.Interval = evaluation.Remaining;
                _automaticTimer.Start();
                return;

            case AutomaticCadenceDecision.Speak:
                ShowEventBubble(CompanionEvent.Automatic, now, fullscreen);
                ArmAutomaticTimer(now, fullscreen);
                return;

            case AutomaticCadenceDecision.NotArmed:
            case AutomaticCadenceDecision.RearmModeChanged:
            case AutomaticCadenceDecision.RearmLate:
                ArmAutomaticTimer(now, fullscreen);
                return;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void EventTimer_Tick(object? sender, EventArgs e) => ProcessEventTimerTick();

    internal void ProcessEventTimerTick()
    {
        if (PresentationSuspended)
        {
            _eventTimer.Stop();
            return;
        }

        var now = LocalNow;
        var fullscreen = ObserveFullscreen();
        if (_automaticCadence.RequiresModeRearm(
                now,
                fullscreen.EffectiveQuietMode))
        {
            ArmAutomaticTimer(now, fullscreen);
            return;
        }

        _eventPump ??= new CompanionEventPump(now, _idleTimeProvider.GetIdleTime());
        var companionEvent = _eventPump.Poll(
            now,
            _idleTimeProvider.GetIdleTime(),
            _dialogue.NextStoryDueAt);
        if (companionEvent is { } trigger)
        {
            ShowEventBubble(trigger, now, fullscreen);
        }
    }

    private bool ShowEventBubble(
        CompanionEvent trigger,
        DateTime localTime,
        FullscreenSnapshot fullscreen)
    {
        if (InteractionFrozen)
        {
            return false;
        }

        if (!_dialogue.IsReady && trigger != CompanionEvent.Startup)
        {
            ObserveDialogueWarmup(replayStartupWhenReady: false);
        }

        var reply = _dialogue.GetReply(trigger, localTime, _random, fullscreen);
        LastReply = reply;
        _dialogueReplyRevision++;
        var displayed = PresentReply(reply);

        if (trigger != CompanionEvent.Automatic && displayed)
        {
            ArmAutomaticTimer(localTime, fullscreen);
        }

        if (_saveAgentMemoryAsync is not null && _dialogue.IsReady)
        {
            _memoryTimer.Stop();
            _memoryTimer.Start();
        }

        return displayed;
    }

    private DateTime LocalNow => _timeProvider.GetLocalNow().LocalDateTime;

    private void ObserveDialogueWarmup(
        bool replayStartupWhenReady,
        bool retryAfterFailure = false)
    {
        _replayStartupAfterWarmupRequested |= replayStartupWhenReady;
        if (InteractionFrozen || _dialogue.IsReady)
        {
            return;
        }

        var warmup = retryAfterFailure
            ? _dialogueWarmup.RetryAfterFailureAsync(_dialogueWarmupLifetime.Token)
            : _dialogueWarmup.StartAsync(_dialogueWarmupLifetime.Token);
        if (ReferenceEquals(warmup, _observedDialogueWarmup))
        {
            return;
        }

        _observedDialogueWarmup = warmup;
        if (_dialogueWarmupViewState == DialogueWarmupViewState.Pending)
        {
            _dialogueWarmupViewState = DialogueWarmupViewState.Loading;
        }

        var generation = ++_dialogueWarmupGeneration;
        _pendingDialogueWarmupOutcome = null;
        _ = CompleteDialogueWarmupAsync(warmup, generation);
    }

    private void RetryDialogueWarmupAfterUserAction()
    {
        if (InteractionFrozen
            || _dialogue.IsReady
            || _dialogueWarmupViewState is not (
                DialogueWarmupViewState.RetryAvailable
                or DialogueWarmupViewState.Retrying))
        {
            return;
        }

        _dialogueWarmupViewState = DialogueWarmupViewState.Retrying;
        _replayUserClickAfterWarmupRequested = true;
        _userRetryFallbackReplyRevision = _dialogueReplyRevision;
        SayMenuItem.Header = "文库正在醒…";
        SayMenuItem.IsEnabled = false;
        AutomationProperties.SetHelpText(
            SayMenuItem,
            "文库正在重试，准备好后就能继续说话。");
        ObserveDialogueWarmup(
            replayStartupWhenReady: false,
            retryAfterFailure: true);
    }

    private async Task CompleteDialogueWarmupAsync(
        Task<DialogueWarmupOutcome> warmup,
        long generation)
    {
        DialogueWarmupOutcome outcome;
        try
        {
            outcome = await warmup.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            Trace.TraceError("Dialogue warmup coordinator failed: {0}", exception);
            return;
        }

        if (outcome is DialogueWarmupOutcome.PermanentFailure
            or DialogueWarmupOutcome.RetriesExhausted)
        {
            if (_dialogueWarmup.LastError is { } error)
            {
                Trace.TraceError("Dialogue warmup stopped after {0}: {1}", outcome, error);
            }
        }
        else if (outcome != DialogueWarmupOutcome.Ready)
        {
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(
                () => ApplyDialogueWarmupOutcome(outcome, generation),
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException) when (
            Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            Trace.TraceError("Could not present the warmed dialogue startup: {0}", exception);
        }
    }

    private void ApplyDialogueWarmupOutcome(
        DialogueWarmupOutcome outcome,
        long generation)
    {
        if (InteractionFrozen || generation != _dialogueWarmupGeneration)
        {
            DiscardPendingDialogueWarmupOutcome(generation);
            return;
        }

        if (generation == _appliedDialogueWarmupGeneration)
        {
            DiscardPendingDialogueWarmupOutcome(generation);
            return;
        }

        if (_isHiddenToTray)
        {
            _pendingDialogueWarmupOutcome = (outcome, generation);
            return;
        }

        DiscardPendingDialogueWarmupOutcome(generation);
        ApplyDialogueWarmupOutcomeVisible(outcome, generation);
    }

    private bool ApplyDialogueWarmupOutcomeVisible(
        DialogueWarmupOutcome outcome,
        long generation,
        DateTime? localTime = null,
        FullscreenSnapshot? fullscreen = null)
    {
        if (InteractionFrozen
            || generation != _dialogueWarmupGeneration
            || generation == _appliedDialogueWarmupGeneration)
        {
            return false;
        }

        _appliedDialogueWarmupGeneration = generation;

        if (outcome == DialogueWarmupOutcome.Ready)
        {
            _dialogueWarmupViewState = DialogueWarmupViewState.Ready;
            SayMenuItem.Header = "说句话 ♡";
            SayMenuItem.IsEnabled = true;
            AutomationProperties.SetHelpText(SayMenuItem, "让佳怡说一句话。");
            var replayUserClick = _replayUserClickAfterWarmupRequested
                && _userRetryFallbackReplyRevision == _dialogueReplyRevision;
            var replayStartup = _replayStartupAfterWarmupRequested
                && _startupFallbackReplyRevision == _dialogueReplyRevision;
            _replayUserClickAfterWarmupRequested = false;
            _replayStartupAfterWarmupRequested = false;
            var replay = replayUserClick
                ? CompanionEvent.Click
                : replayStartup
                    ? CompanionEvent.Startup
                    : (CompanionEvent?)null;
            if (replay is { } trigger)
            {
                var decisionTime = localTime ?? LocalNow;
                var decisionFullscreen = fullscreen ?? ObserveFullscreen();
                return ShowEventBubble(trigger, decisionTime, decisionFullscreen);
            }

            return false;
        }

        _dialogueWarmupViewState = DialogueWarmupViewState.RetryAvailable;
        _replayUserClickAfterWarmupRequested = false;
        SayMenuItem.Header = "重试文库 ♡";
        SayMenuItem.IsEnabled = true;
        AutomationProperties.SetHelpText(SayMenuItem, "重新加载佳怡的文库。");
        ShowBubble(DialogueWarmupFailureMessage);
        return false;
    }

    private bool ConsumePendingDialogueWarmupOutcome(
        DateTime localTime,
        FullscreenSnapshot fullscreen)
    {
        CaptureCompletedDialogueWarmupOutcome();
        var pending = _pendingDialogueWarmupOutcome;
        _pendingDialogueWarmupOutcome = null;
        return pending is { } value
            && value.Generation == _dialogueWarmupGeneration
            && ApplyDialogueWarmupOutcomeVisible(
                value.Outcome,
                value.Generation,
                localTime,
                fullscreen);
    }

    private void CaptureCompletedDialogueWarmupOutcome()
    {
        if (_pendingDialogueWarmupOutcome is not null
            || _dialogueWarmupGeneration == _appliedDialogueWarmupGeneration
            || _observedDialogueWarmup is not { IsCompletedSuccessfully: true } completed)
        {
            return;
        }

        var outcome = completed.Result;
        if (outcome is DialogueWarmupOutcome.Ready
            or DialogueWarmupOutcome.PermanentFailure
            or DialogueWarmupOutcome.RetriesExhausted)
        {
            _pendingDialogueWarmupOutcome = (outcome, _dialogueWarmupGeneration);
        }
    }

    private void DiscardPendingDialogueWarmupOutcome(long generation)
    {
        if (_pendingDialogueWarmupOutcome is { Generation: var pendingGeneration }
            && pendingGeneration == generation)
        {
            _pendingDialogueWarmupOutcome = null;
        }
    }

    internal bool PresentReply(AgentReply reply)
    {
        if (reply.ShouldDisplayText)
        {
            ShowBubble(
                reply.Text,
                speak: reply.SourceLine?.SourceKind != "builtin_fallback",
                tone: reply.SourceLine?.Tone,
                trigger: reply.SourceLine?.Trigger.ToString(),
                urgent: reply.Trigger is CompanionEvent.Click or CompanionEvent.Startup);
            if (!InteractionFrozen && reply.SourceLine?.SourceKind != "builtin_fallback")
            {
                NoteCorpusLine(reply.Text);
            }
        }
        else if (reply.Trigger == CompanionEvent.Click)
        {
            HideBubble();
        }

        return reply.ShouldDisplayText;
    }

    internal async void MemoryTimer_Tick(object? sender, EventArgs e)
    {
        _memoryTimer.Stop();
        if (InteractionFrozen)
        {
            return;
        }

        await SaveAgentMemoryAsync(skipWhenExiting: true);
    }

    internal void SaySomething()
    {
        if (_isHiddenToTray)
        {
            RestoreVisibleWindow();
        }

        ReactAndSpeak();
    }

    private void SaySomething_Click(object sender, RoutedEventArgs e) => SaySomething();

    private void Greeting_Click(object sender, RoutedEventArgs e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        PreserveScheduledStartupGreeting();
        InvalidateAmbientSchedule();
        if (_actionCoordinator.TryBeginAmbient(PetAmbientAction.Greeting))
        {
            _animation.PlayGreeting(
                () => CompleteAmbientAction(PetActionState.Greeting));
        }
    }

    private async void ToggleAnimation_Click(object sender, RoutedEventArgs e)
    {
        await ToggleAnimationAsync();
    }

    internal async Task ToggleAnimationAsync()
    {
        if (InteractionFrozen)
        {
            return;
        }

        _paused = !_paused;
        if (_paused)
        {
            PreserveScheduledStartupGreeting();
            InvalidateAmbientSchedule();
            CancelActiveAmbientAction();
            _actionCoordinator.Pause();
            _animation.PauseIdle();
        }
        else
        {
            _animation.ResumeIdle();
            _actionCoordinator.Resume();
            ScheduleNextAmbientAction();
        }

        UpdatePauseLabel();
        if (!PresentationSuspended)
        {
            ShowEventBubble(
                _paused ? CompanionEvent.AnimationPaused : CompanionEvent.AnimationResumed,
                LocalNow,
                ObserveFullscreen());
        }

        await SaveSettingsAsync(skipWhenExiting: true);
    }

    private void UpdatePauseLabel() =>
        PauseMenuItem.Header = _paused ? "继续动画" : "暂停动画";

    private async void SetSize_Click(object sender, RoutedEventArgs e)
    {
        if (InteractionFrozen
            || sender is not MenuItem { Tag: string tag }
            || !Enum.TryParse(tag, out PetScale scale))
        {
            return;
        }

        _scale = scale;
        var previousCharacter = GetCharacterScreenBounds();
        ApplyScale(scale);
        UpdateLayout();
        var localBounds = GetCharacterLocalBounds();
        var requested = new ScreenPoint(
            previousCharacter.Left + (previousCharacter.Width / 2)
                - localBounds.Left - (localBounds.Width / 2),
            previousCharacter.Bottom - localBounds.Bottom);
        var workAreas = WorkAreaService.GetWorkAreas();
        var clamped = ScreenPlacementService.ClampVisibleBounds(
            requested,
            localBounds,
            workAreas);
        Left = clamped.X;
        Top = clamped.Y;
        PositionBubble();
        PositionDialogue();
        ShowEventBubble(CompanionEvent.SizeChanged, LocalNow, ObserveFullscreen());
        await SaveSettingsAsync(skipWhenExiting: true);
    }

    internal void ApplyScale(PetScale scale)
    {
        var size = scale switch
        {
            PetScale.Small => 250,
            PetScale.Large => 390,
            _ => 320
        };
        CharacterStage.Width = size;
        CharacterStage.Height = size;
        SmallSizeMenuItem.IsChecked = scale == PetScale.Small;
        NormalSizeMenuItem.IsChecked = scale == PetScale.Normal;
        LargeSizeMenuItem.IsChecked = scale == PetScale.Large;
    }

    private async void ToggleTopmost_Click(object sender, RoutedEventArgs e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        Topmost = TopmostMenuItem.IsChecked;
        if (_dialogueComposer is not null)
        {
            _dialogueComposer.Topmost = Topmost;
        }

        await SaveSettingsAsync(skipWhenExiting: true);
    }

    private async void RestorePosition_Click(object sender, RoutedEventArgs e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        var workAreas = WorkAreaService.GetWorkAreas();
        var work = workAreas.Count > 0
            ? workAreas[0]
            : new ScreenRect(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);
        var point = DefaultPosition(work, GetCharacterLocalBounds());
        Left = point.X;
        Top = point.Y;
        ShowEventBubble(CompanionEvent.PositionRestored, LocalNow, ObserveFullscreen());
        await SaveSettingsAsync(skipWhenExiting: true);
    }

    internal void HideToTray()
    {
        if (!InteractionFrozen && _trayAvailable && !_isHiddenToTray)
        {
            _isHiddenToTray = true;
            ControlMenu.IsOpen = false;
            DisarmAutomaticTimer();
            _eventTimer.Stop();
            PreserveScheduledStartupGreeting();
            InvalidateAmbientSchedule();
            _animation.Suspend();
            _bubbleCountdown.Suspend();
            _bubbleTimer.Stop();
            _bubbleSuspendedForWindowHide = BubblePopup.IsOpen;
            BubblePopup.IsOpen = false;
            _dialogueComposer?.Hide();
            StopVoicePlayback();
            Hide();
        }
    }

    internal void SetTrayAvailability(bool available)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetTrayAvailability(available));
            return;
        }

        if (InteractionFrozen && available)
        {
            return;
        }

        _trayAvailable = available;
        UpdateTrayAvailabilityControls();
        if (!available && !_isClosed && !_exitCommandRunning)
        {
            RestoreVisibleWindow();
        }
    }

    private void UpdateTrayAvailabilityControls()
    {
        HideToTrayMenuItem.IsEnabled = _trayAvailable;
        var unavailableReason = "托盘暂时不可用，桌宠会保持显示。";
        HideToTrayMenuItem.ToolTip = _trayAvailable
            ? null
            : unavailableReason;
        AutomationProperties.SetHelpText(
            HideToTrayMenuItem,
            _trayAvailable ? "把佳怡藏到系统托盘。" : unavailableReason);
    }

    internal void ToggleVisibilityFromTray()
    {
        if (InteractionFrozen)
        {
            return;
        }

        if (!_trayAvailable)
        {
            RestoreVisibleWindow();
            return;
        }

        if (IsVisible)
        {
            HideToTray();
            return;
        }

        RestoreVisibleWindow();
    }

    internal void RestoreFromSecondInstance()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RestoreFromSecondInstance);
            return;
        }

        if (!InteractionFrozen)
        {
            RestoreVisibleWindow();
        }
    }

    private void RestoreVisibleWindow()
    {
        var resumingFromTray = _isHiddenToTray;
        _isHiddenToTray = false;
        Show();
        WindowState = WindowState.Normal;
        UpdateLayout();
        EnsureCurrentPositionIsVisible();
        var restoreTime = default(DateTime);
        var restoreFullscreen = default(FullscreenSnapshot);
        if (resumingFromTray)
        {
            restoreTime = LocalNow;
            restoreFullscreen = ObserveFullscreen();
        }

        if (_bubbleSuspendedForWindowHide
            && SpeechBubble.Visibility == Visibility.Visible)
        {
            BubblePopup.IsOpen = true;
            _announceLiveRegionChanged(SpeechText);
        }

        _bubbleSuspendedForWindowHide = false;
        _animation.Resume();
        _bubbleCountdown.Resume();
        SynchronizeBubbleTimer();
        PositionBubble();
        if (DialogueMenuItem.IsChecked)
        {
            UpdateDialogueComposer();
        }
        if (resumingFromTray)
        {
            if (_deferredVoiceClip is { } deferred)
            {
                _deferredVoiceClip = null;
                OnVoicePlaybackReady(deferred);
            }

            var replayDisplayed = ConsumePendingDialogueWarmupOutcome(
                restoreTime,
                restoreFullscreen);
            if (!replayDisplayed)
            {
                ArmAutomaticTimer(restoreTime, restoreFullscreen);
            }

            _eventTimer.Start();
            ScheduleNextAmbientAction();
        }

        Activate();
        Dispatcher.BeginInvoke(
            () => CharacterStage.Focus(),
            DispatcherPriority.Input);
    }

    internal TrayMenuState GetTrayMenuState()
    {
        if (_autoStartService.TryGetEnabled(out var enabled))
        {
            SetKnownAutoStartState(enabled);
            return new TrayMenuState(IsVisible, _paused, enabled, true);
        }

        MarkAutoStartUnavailable();
        return new TrayMenuState(IsVisible, _paused, _lastKnownAutoStart, false);
    }

    internal bool TryReadAutoStart(out bool enabled) =>
        _autoStartService.TryGetEnabled(out enabled);

    internal void ToggleAutoStartFromTray()
    {
        if (InteractionFrozen)
        {
            return;
        }

        if (!_autoStartService.TryGetEnabled(out var current))
        {
            MarkAutoStartUnavailable();
            ShowLocalFeedbackWhenVisible("Windows 暂时不允许读取开机启动设置。");
            return;
        }

        SetKnownAutoStartState(current);
        ApplyAutoStart(!current);
    }

    private void CharacterStage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (InteractionFrozen
            || (e.Key != Key.Apps
                && (e.Key != Key.F10 || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))))
        {
            return;
        }

        _controlMenuFocusReturnTarget = GetControlMenuFocusReturnTarget();
        _controlMenuOpenedFromKeyboard = true;
        ControlMenu.PlacementTarget = CharacterStage;
        ControlMenu.Placement = PlacementMode.Custom;
        ControlMenu.HorizontalOffset = 0;
        ControlMenu.VerticalOffset = 0;
        ControlMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ControlMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (InteractionFrozen)
        {
            ControlMenu.IsOpen = false;
            return;
        }

        _controlMenuFocusReturnTarget ??= GetControlMenuFocusReturnTarget();
        RefreshAutoStartState();
        WriteDeveloperDraft(_developerParameters);
        Dispatcher.BeginInvoke(
            () =>
            {
                if (!ControlMenu.IsOpen)
                {
                    return;
                }

                if (_controlMenuPlacementReady)
                {
                    AlignSubmenuPopups(ControlMenu, _controlMenuPlacement.SubmenuOpensRight);
                }

                SayMenuItem.Focus();
            },
            DispatcherPriority.Input);
    }

    private CustomPopupPlacement[] PlaceControlMenu(Size popupSize, Size targetSize, Point offset)
    {
        _ = targetSize;
        _ = offset;
        var menu = ToDipSize(popupSize);
        var character = GetCharacterScreenBounds();
        var placement = ControlMenuPlacementService.Place(
            character,
            menu,
            GetControlMenuWorkArea(character));
        _controlMenuPlacement = placement;
        _controlMenuPlacementReady = true;
        return
        [
            new CustomPopupPlacement(
                ToDeviceOffset(placement.Origin, character),
                PopupPrimaryAxis.None)
        ];
    }

    private ScreenRect GetControlMenuWorkArea(ScreenRect character)
    {
        var workAreas = WorkAreaService.GetWorkAreas();
        if (workAreas.Count == 0)
        {
            var work = SystemParameters.WorkArea;
            return new ScreenRect(work.Left, work.Top, work.Width, work.Height);
        }

        var center = new ScreenPoint(
            character.Left + (character.Width / 2),
            character.Top + (character.Height / 2));
        var workArea = workAreas.FirstOrDefault(area => area.Contains(center));
        return workArea.Width > 0 ? workArea : workAreas[0];
    }

    private ScreenSize ToDipSize(Size deviceSize)
    {
        var source = PresentationSource.FromVisual(this);
        if (source is null)
        {
            return new ScreenSize(deviceSize.Width, deviceSize.Height);
        }

        var dip = source.CompositionTarget.TransformFromDevice.Transform(
            new Vector(deviceSize.Width, deviceSize.Height));
        return new ScreenSize(Math.Max(0, dip.X), Math.Max(0, dip.Y));
    }

    private Point ToDeviceOffset(ScreenPoint menuOrigin, ScreenRect character)
    {
        var source = PresentationSource.FromVisual(this);
        if (source is null)
        {
            return new Point(menuOrigin.X - character.Left, menuOrigin.Y - character.Top);
        }

        var toDevice = source.CompositionTarget.TransformToDevice;
        var menu = toDevice.Transform(new Point(menuOrigin.X, menuOrigin.Y));
        var origin = toDevice.Transform(new Point(character.Left, character.Top));
        return new Point(menu.X - origin.X, menu.Y - origin.Y);
    }

    private void AlignSubmenuPopups(ItemsControl menu, bool opensRight)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.ApplyTemplate();
            if (item.Template?.FindName("PART_Popup", item) is Popup popup)
            {
                popup.Placement = opensRight ? PlacementMode.Right : PlacementMode.Left;
            }

            if (item.HasItems
                && item.Template?.FindName("SubmenuArrow", item) is TextBlock arrow)
            {
                arrow.Text = opensRight ? "›" : "‹";
            }

            item.SubmenuOpened -= ControlSubmenuOpened;
            item.SubmenuOpened += ControlSubmenuOpened;
        }
    }

    private void ControlSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (_controlMenuPlacementReady && sender is MenuItem item)
        {
            AlignSubmenuPopups(item, _controlMenuPlacement.SubmenuOpensRight);
        }
    }

    private void BrowseAllCorpus_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(
            () => OpenDeveloperWindow(CorpusBrowserIndex.Root),
            DispatcherPriority.Input);

    private void DeveloperModeMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        item.ApplyTemplate();
        if (item.Template.FindName("DeveloperSubmenuScroll", item) is ScrollViewer scroll)
        {
            scroll.MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height - 96);
        }
    }

    private bool _writingDeveloperSliders;

    private void DeveloperRange_Changed(object? sender, EventArgs e)
    {
        if (_writingDeveloperSliders)
        {
            return;
        }

        RefreshDeveloperSliderLabels();
    }

    private void ApplyParameters_Click(object sender, RoutedEventArgs e) => ApplyDeveloperDraft(keepStatus: false);

    private void ResetParameters_Click(object sender, RoutedEventArgs e)
    {
        WriteDeveloperDraft(DeveloperTestParameters.CreateDefault());
        ApplyDeveloperDraft(keepStatus: true);
    }

    private void CorpusMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (_corpusMenuBuilt || _isClosed)
        {
            return;
        }

        _corpusMenuBuilt = true;
        var root = CorpusBrowserIndex.Root;
        BrowseAllCorpusMenuItem.Header = $"全库语句  {root.Lines.Length}";
        foreach (var child in root.Children)
        {
            CorpusMenuItem.Items.Add(CreateFolderMenu(child));
        }

        if (_controlMenuPlacementReady)
        {
            AlignSubmenuPopups(CorpusMenuItem, _controlMenuPlacement.SubmenuOpensRight);
        }
    }

    private MenuItem CreateFolderMenu(CorpusFolder folder)
    {
        var item = new MenuItem
        {
            Header = folder.Header,
            Tag = "·",
            Style = MenuItemStyle
        };
        item.Items.Add(new MenuItem
        {
            Header = "…",
            IsEnabled = false,
            Style = MenuItemStyle
        });
        item.SubmenuOpened += (_, _) => PopulateFolder(item, folder);
        return item;
    }

    private void PopulateFolder(MenuItem item, CorpusFolder folder)
    {
        if (!_populatedCorpusMenus.Add(item))
        {
            return;
        }

        item.Items.Clear();
        item.Items.Add(CreateBrowseItem(folder));
        if (folder.Children.Length == 0)
        {
            AddLineItems(item, folder);
        }
        else
        {
            item.Items.Add(CreateSeparator());
            foreach (var child in folder.Children)
            {
                item.Items.Add(CreateFolderMenu(child));
            }
        }

        if (_controlMenuPlacementReady)
        {
            AlignSubmenuPopups(item, _controlMenuPlacement.SubmenuOpensRight);
        }
    }

    private MenuItem CreateBrowseItem(CorpusFolder folder)
    {
        var item = new MenuItem
        {
            Header = $"查看全部 {folder.Lines.Length} 条",
            Tag = "☰",
            Style = MenuItemStyle
        };
        item.Click += (_, _) => Dispatcher.BeginInvoke(
            () => OpenDeveloperWindow(folder),
            DispatcherPriority.Input);
        return item;
    }

    private void AddLineItems(MenuItem item, CorpusFolder folder)
    {
        const int menuLineCap = 40;
        var shown = Math.Min(menuLineCap, folder.Lines.Length);
        if (shown > 0)
        {
            item.Items.Add(CreateSeparator());
        }

        for (var index = 0; index < shown; index++)
        {
            var line = folder.Lines[index];
            var header = line.Text.Length <= 42 ? line.Text : string.Concat(line.Text.AsSpan(0, 42), "…");
            var entry = new MenuItem
            {
                Header = header,
                Tag = "✦",
                ToolTip = line.Text,
                Style = MenuItemStyle
            };
            entry.Click += (_, _) => SpeakDeveloperLine(line);
            item.Items.Add(entry);
        }

        if (folder.Lines.Length > shown)
        {
            var rest = folder.Lines.Length - shown;
            var more = new MenuItem
            {
                Header = $"还有 {rest} 条，打开窗口查看",
                Tag = "☰",
                Style = MenuItemStyle
            };
            more.Click += (_, _) => Dispatcher.BeginInvoke(
                () => OpenDeveloperWindow(folder),
                DispatcherPriority.Input);
            item.Items.Add(more);
        }
    }

    private void OpenDeveloperWindow(CorpusFolder folder)
    {
        if (InteractionFrozen)
        {
            return;
        }

        if (_developerWindow is { IsLoaded: true })
        {
            _developerWindow.SelectFolder(folder);
            _developerWindow.Activate();
            return;
        }

        var window = new DeveloperModeWindow(CorpusBrowserIndex.Root, SpeakDeveloperLine)
        {
            Owner = this,
            Topmost = Topmost
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_developerWindow, window))
            {
                _developerWindow = null;
            }
        };
        _developerWindow = window;
        window.SelectFolder(folder);
        window.Show();
    }

    private void BrowseVoiceLibrary_Click(object sender, RoutedEventArgs e)
    {
        var library = VoiceLibraryIndex.TryLoad(AppContext.BaseDirectory);
        if (library is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => OpenVoiceLibrary(library), DispatcherPriority.Input);
    }

    private void VoiceLibraryMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (_voiceLibraryMenuBuilt || _isClosed)
        {
            return;
        }

        _voiceLibraryMenuBuilt = true;
        var library = VoiceLibraryIndex.TryLoad(AppContext.BaseDirectory);
        if (library is null)
        {
            BrowseVoiceLibraryMenuItem.Header = "旁边还没有语音库";
            BrowseVoiceLibraryMenuItem.IsEnabled = false;
            return;
        }

        BrowseVoiceLibraryMenuItem.Header = $"全部参考  {library.Clips.Length}";
        foreach (var child in library.Children)
        {
            var folder = child;
            var item = new MenuItem
            {
                Header = folder.Header,
                Tag = "·",
                Style = MenuItemStyle
            };
            item.Click += (_, _) => Dispatcher.BeginInvoke(
                () => OpenVoiceLibrary(folder, library),
                DispatcherPriority.Input);
            VoiceLibraryMenuItem.Items.Add(item);
        }

        if (_controlMenuPlacementReady)
        {
            AlignSubmenuPopups(VoiceLibraryMenuItem, _controlMenuPlacement.SubmenuOpensRight);
        }
    }

    private void OpenVoiceLibrary(VoiceLibraryFolder folder, VoiceLibraryFolder? root = null)
    {
        if (InteractionFrozen)
        {
            return;
        }

        root ??= folder;
        if (_voiceLibraryWindow is { IsLoaded: true })
        {
            _voiceLibraryWindow.SelectFolder(folder);
            _voiceLibraryWindow.Activate();
            return;
        }

        var window = new VoiceLibraryWindow(root)
        {
            Owner = this,
            Topmost = Topmost
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_voiceLibraryWindow, window))
            {
                _voiceLibraryWindow = null;
            }
        };
        _voiceLibraryWindow = window;
        window.SelectFolder(folder);
        window.Show();
    }

    private void SpeakDeveloperLine(DialogueLine line)
    {
        if (InteractionFrozen || string.IsNullOrWhiteSpace(line.Text))
        {
            return;
        }

        ShowBubble(line.Text, speak: true, tone: line.Tone, trigger: line.Trigger.ToString(), urgent: true);
        NoteCorpusLine(line.Text);
    }

    private PetReminderStore Reminders => _reminders ??= new PetReminderStore();

    private void ReminderTimer_Tick(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        var line = Reminders.TakeDue(DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(line))
        {
            ShowBubble(line, speak: true, tone: "gentle");
        }
    }

    private void ApplyDialogueActions(string actionsJson)
    {
        Reminders.Accept(actionsJson, DateTime.UtcNow);
        if (!DialogueSkills.TryApply(
                actionsJson,
                _developerParameters,
                _scale,
                Topmost,
                _paused,
                _voice is { Enabled: true },
                out var state)
            || !state.Changed)
        {
            return;
        }

        ApplyDeveloperParameters(state.Timing);
        if (_scale != state.Scale)
        {
            ResizeWithoutAnnouncement(state.Scale);
        }

        if (Topmost != state.AlwaysOnTop)
        {
            Topmost = state.AlwaysOnTop;
            TopmostMenuItem.IsChecked = state.AlwaysOnTop;
            if (_dialogueComposer is not null)
            {
                _dialogueComposer.Topmost = state.AlwaysOnTop;
            }
        }

        if (_paused != state.AnimationPaused)
        {
            SetAnimationPaused(state.AnimationPaused);
        }

        if (_voice is not null && _voice.Enabled != state.VoiceEnabled)
        {
            _voice.Enabled = state.VoiceEnabled;
            VoiceMenuItem.IsChecked = state.VoiceEnabled;
            if (!state.VoiceEnabled)
            {
                StopVoicePlayback();
            }
        }

        WriteDeveloperDraft(state.Timing);
        _ = SaveSettingsAsync(skipWhenExiting: true);
    }

    private void ResizeWithoutAnnouncement(PetScale scale)
    {
        _scale = scale;
        var previousCharacter = GetCharacterScreenBounds();
        ApplyScale(scale);
        UpdateLayout();
        var localBounds = GetCharacterLocalBounds();
        var requested = new ScreenPoint(
            previousCharacter.Left + (previousCharacter.Width / 2)
                - localBounds.Left - (localBounds.Width / 2),
            previousCharacter.Bottom - localBounds.Bottom);
        var clamped = ScreenPlacementService.ClampVisibleBounds(
            requested,
            localBounds,
            WorkAreaService.GetWorkAreas());
        Left = clamped.X;
        Top = clamped.Y;
        PositionBubble();
        PositionDialogue();
    }

    private void SetAnimationPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        if (_paused)
        {
            PreserveScheduledStartupGreeting();
            InvalidateAmbientSchedule();
            CancelActiveAmbientAction();
            _actionCoordinator.Pause();
            _animation.PauseIdle();
        }
        else
        {
            _animation.ResumeIdle();
            _actionCoordinator.Resume();
            ScheduleNextAmbientAction();
        }

        UpdatePauseLabel();
    }

    private void ApplyDeveloperParameters(DeveloperTestParameters draft)
    {
        _developerParameters.CopyFrom(draft);
        _dialogueScheduler.TestParameters = _developerParameters;
        _bubbleCountdown.ActiveDisplayDuration = TimeSpan.FromSeconds(_developerParameters.BubbleSeconds);
        _voice?.ApplySpeech(
            _developerParameters.SpeechSpeed,
            _developerParameters.SpeechTemperature,
            _developerParameters.SpeechRepetition,
            _developerParameters.TopK,
            _developerParameters.TopP);
        if (!PresentationSuspended)
        {
            ArmAutomaticTimer(LocalNow, ObserveFullscreen());
        }
    }

    private void ApplyDeveloperDraft(bool keepStatus)
    {
        if (!TryReadDeveloperDraft(out var draft, out var error))
        {
            ParameterErrorText.Text = error;
            ParameterStatusText.Text = string.Empty;
            return;
        }

        var validation = draft.Validate();
        if (validation is not null)
        {
            ParameterErrorText.Text = validation;
            ParameterStatusText.Text = string.Empty;
            return;
        }

        ParameterErrorText.Text = string.Empty;
        ApplyDeveloperParameters(draft);
        _ = SaveSettingsAsync(skipWhenExiting: true);
        var voiceOn = _voice is { Enabled: true };
        ParameterStatusText.Text = keepStatus
            ? voiceOn
                ? "已经回到原来的间隔、气泡和语音。"
                : "已经回到原来的间隔和气泡时间。"
            : voiceOn
                ? "已经换成这组啦，下一句按这个语速、语气和字数。"
                : "已经换成这组啦，输出按这个字数。";
    }

    private bool TryReadDeveloperDraft(out DeveloperTestParameters draft, out string error)
    {
        draft = _developerParameters.Clone();
        draft.DayMinimumMinutes = ReadSliderInt(DayRange.LowerValue);
        draft.DayMaximumMinutes = ReadSliderInt(DayRange.UpperValue);
        draft.EveningMinimumMinutes = ReadSliderInt(EveningRange.LowerValue);
        draft.EveningMaximumMinutes = ReadSliderInt(EveningRange.UpperValue);
        draft.LateNightMinimumMinutes = ReadSliderInt(LateRange.LowerValue);
        draft.LateNightMaximumMinutes = ReadSliderInt(LateRange.UpperValue);
        draft.FullscreenMinimumMinutes = ReadSliderInt(FullscreenRange.LowerValue);
        draft.FullscreenMaximumMinutes = ReadSliderInt(FullscreenRange.UpperValue);
        draft.BubbleSeconds = ReadSliderInt(BubbleSlider.LowerValue);
        draft.ReplyMaxChars = ReadSliderInt(ReplyMaxCharsSlider.LowerValue);
        if (VoiceParameterPanel.Visibility == Visibility.Visible)
        {
            draft.SpeechSpeed = ReadSliderNumber(SpeechSpeedSlider.LowerValue);
            draft.SpeechTemperature = ReadSliderNumber(SpeechTemperatureSlider.LowerValue);
            draft.SpeechRepetition = ReadSliderNumber(SpeechRepetitionSlider.LowerValue);
        }

        error = string.Empty;
        return true;
    }

    private static int ReadSliderInt(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static double ReadSliderNumber(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private void UpdateVoiceParameterPanel()
    {
        VoiceParameterPanel.Visibility = _voice is { Enabled: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void WriteDeveloperDraft(DeveloperTestParameters parameters)
    {
        _writingDeveloperSliders = true;
        try
        {
            DayRange.SetBounds(DeveloperTestParameters.MinimumDayMinutes, DeveloperTestParameters.MaximumDayMinutes, parameters.DayMinimumMinutes, parameters.DayMaximumMinutes, 1);
            EveningRange.SetBounds(DeveloperTestParameters.MinimumEveningMinutes, DeveloperTestParameters.MaximumEveningMinutes, parameters.EveningMinimumMinutes, parameters.EveningMaximumMinutes, 1);
            LateRange.SetBounds(DeveloperTestParameters.MinimumLateNightMinutes, DeveloperTestParameters.MaximumLateNightMinutes, parameters.LateNightMinimumMinutes, parameters.LateNightMaximumMinutes, 1);
            FullscreenRange.SetBounds(DeveloperTestParameters.MinimumFullscreenMinutes, DeveloperTestParameters.MaximumFullscreenMinutes, parameters.FullscreenMinimumMinutes, parameters.FullscreenMaximumMinutes, 1);
            BubbleSlider.SetBounds(DeveloperTestParameters.MinimumBubbleSeconds, DeveloperTestParameters.MaximumBubbleSeconds, parameters.BubbleSeconds, parameters.BubbleSeconds, 1);
            ReplyMaxCharsSlider.SetBounds(DeveloperTestParameters.MinimumReplyChars, DeveloperTestParameters.MaximumReplyChars, parameters.ReplyMaxChars, parameters.ReplyMaxChars, 1);
            SpeechSpeedSlider.SetBounds(DeveloperTestParameters.MinimumSpeechSpeed, DeveloperTestParameters.MaximumSpeechSpeed, parameters.SpeechSpeed, parameters.SpeechSpeed, 0.1);
            SpeechTemperatureSlider.SetBounds(DeveloperTestParameters.MinimumSpeechTemperature, DeveloperTestParameters.MaximumSpeechTemperature, parameters.SpeechTemperature, parameters.SpeechTemperature, 0.1);
            SpeechRepetitionSlider.SetBounds(DeveloperTestParameters.MinimumSpeechRepetition, DeveloperTestParameters.MaximumSpeechRepetition, parameters.SpeechRepetition, parameters.SpeechRepetition, 0.1);
        }
        finally
        {
            _writingDeveloperSliders = false;
        }

        RefreshDeveloperSliderLabels();
        ParameterErrorText.Text = string.Empty;
        ParameterStatusText.Text = string.Empty;
        UpdateVoiceParameterPanel();
    }

    private void RefreshDeveloperSliderLabels()
    {
        if (DayRangeText is null
            || EveningRangeText is null
            || LateRangeText is null
            || FullscreenRangeText is null
            || BubbleValueText is null
            || ReplyValueText is null
            || SpeechSpeedValueText is null
            || SpeechTemperatureValueText is null
            || SpeechRepetitionValueText is null
            || DayRange is null
            || EveningRange is null
            || LateRange is null
            || FullscreenRange is null
            || BubbleSlider is null
            || ReplyMaxCharsSlider is null
            || SpeechSpeedSlider is null
            || SpeechTemperatureSlider is null
            || SpeechRepetitionSlider is null)
        {
            return;
        }

        DayRangeText.Text = $"{ReadSliderInt(DayRange.LowerValue)} 到 {ReadSliderInt(DayRange.UpperValue)} 分钟";
        EveningRangeText.Text = $"{ReadSliderInt(EveningRange.LowerValue)} 到 {ReadSliderInt(EveningRange.UpperValue)} 分钟";
        LateRangeText.Text = $"{ReadSliderInt(LateRange.LowerValue)} 到 {ReadSliderInt(LateRange.UpperValue)} 分钟";
        FullscreenRangeText.Text = $"{ReadSliderInt(FullscreenRange.LowerValue)} 到 {ReadSliderInt(FullscreenRange.UpperValue)} 分钟";
        BubbleValueText.Text = $"{ReadSliderInt(BubbleSlider.LowerValue)} 秒";
        ReplyValueText.Text = $"{ReadSliderInt(ReplyMaxCharsSlider.LowerValue)} 字";
        SpeechSpeedValueText.Text = $"{ReadSliderNumber(SpeechSpeedSlider.LowerValue).ToString("0.##", CultureInfo.InvariantCulture)} 倍";
        SpeechTemperatureValueText.Text = ReadSliderNumber(SpeechTemperatureSlider.LowerValue).ToString("0.##", CultureInfo.InvariantCulture);
        SpeechRepetitionValueText.Text = ReadSliderNumber(SpeechRepetitionSlider.LowerValue).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private Style MenuItemStyle => (Style)ControlMenu.FindResource(typeof(MenuItem));

    private Separator CreateSeparator() => new()
    {
        Style = (Style)ControlMenu.FindResource(typeof(Separator))
    };

    internal DialogueScheduler DialogueSchedulerForTests => _dialogueScheduler;

    private static void RaiseLiveRegionChanged(FrameworkElement element)
    {
        var peer = UIElementAutomationPeer.FromElement(element)
            ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void ControlMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        ControlMenu.IsOpen = false;
        e.Handled = true;
    }

    private IInputElement GetControlMenuFocusReturnTarget() =>
        Keyboard.FocusedElement is DependencyObject focusedElement
        && ReferenceEquals(GetWindow(focusedElement), this)
            ? (IInputElement)focusedElement
            : CharacterStage;

    private void ControlMenu_Closed(object sender, RoutedEventArgs e)
    {
        _controlMenuPlacementReady = false;
        if (_controlMenuOpenedFromKeyboard)
        {
            ControlMenu.ClearValue(ContextMenu.PlacementProperty);
            ControlMenu.ClearValue(ContextMenu.PlacementTargetProperty);
            _controlMenuOpenedFromKeyboard = false;
        }

        var focusTarget = _controlMenuFocusReturnTarget;
        _controlMenuFocusReturnTarget = null;
        if (InteractionFrozen || !IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                if (InteractionFrozen || !IsVisible)
                {
                    return;
                }

                Activate();
                if (focusTarget is UIElement { IsEnabled: true, IsVisible: true } element
                    && element.Focus())
                {
                    return;
                }

                CharacterStage.Focus();
            },
            DispatcherPriority.Input);
    }

    private void ToggleAutoStart_Click(object sender, RoutedEventArgs e) =>
        ApplyAutoStart(AutoStartMenuItem.IsChecked);

    private void HideToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void RefreshAutoStartState()
    {
        if (_autoStartService.TryGetEnabled(out var enabled))
        {
            SetKnownAutoStartState(enabled);
            return;
        }

        MarkAutoStartUnavailable();
    }

    private void SetKnownAutoStartState(bool enabled)
    {
        _lastKnownAutoStart = enabled;
        AutoStartMenuItem.IsChecked = enabled;
        AutoStartMenuItem.IsEnabled = true;
        AutoStartMenuItem.ToolTip = null;
        AutomationProperties.SetHelpText(
            AutoStartMenuItem,
            "切换是否跟随 Windows 开机启动。");
    }

    private void MarkAutoStartUnavailable()
    {
        const string unavailableReason = "Windows 暂时不允许读取开机启动设置。";
        AutoStartMenuItem.IsChecked = _lastKnownAutoStart;
        AutoStartMenuItem.IsEnabled = false;
        AutoStartMenuItem.ToolTip = unavailableReason;
        AutomationProperties.SetHelpText(AutoStartMenuItem, unavailableReason);
    }

    private void ApplyAutoStart(bool requested)
    {
        if (InteractionFrozen)
        {
            return;
        }

        var previous = _lastKnownAutoStart;
        if (_autoStartService.TrySetEnabled(requested))
        {
            _lastKnownAutoStart = requested;
            AutoStartMenuItem.IsChecked = requested;
            RefreshAutoStartState();
            return;
        }

        _lastKnownAutoStart = previous;
        AutoStartMenuItem.IsChecked = previous;
        ShowLocalFeedbackWhenVisible("开机启动没设置上，Windows 不让改。");
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RequestExitAsync();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Window exit failed after close: {0}", exception);
        }
    }

    internal async Task RequestExitAsync()
    {
        if (_exitCommandRunning || _isClosed)
        {
            return;
        }

        _exitCommandRunning = true;
        FreezeInteractionForExit();
        try
        {
            await SaveForExitBestEffortAsync(() => SaveSettingsAsync(), "settings");
            await SaveForExitBestEffortAsync(() => SaveAgentMemoryAsync(), "agent memory");
        }
        finally
        {
            if (!_isClosed)
            {
                Close();
            }
        }
    }

    private static async Task SaveForExitBestEffortAsync(
        Func<Task> save,
        string description)
    {
        try
        {
            await save();
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            Trace.TraceError("Could not save {0} during exit: {1}", description, exception);
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    private void FreezeInteractionForExit()
    {
        _pendingDialogueWarmupOutcome = null;
        DisarmAutomaticTimer();
        _eventTimer.Stop();
        _memoryTimer.Stop();
        _reminderTimer.Stop();
        _bubbleTimer.Stop();
        _bubbleCountdown.Close();
        PreserveScheduledStartupGreeting();
        InvalidateAmbientSchedule();
        CancelActiveAmbientAction();
    }

    private async Task SaveAgentMemoryAsync(bool skipWhenExiting = false)
    {
        if (_saveAgentMemoryAsync is null || !_dialogue.IsReady)
        {
            return;
        }

        await _memorySaveGate.WaitAsync();
        try
        {
            if (skipWhenExiting && InteractionFrozen)
            {
                return;
            }

            await _saveAgentMemoryAsync(_dialogue.CreateSnapshot());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _memorySaveGate.Release();
        }
    }

    private async Task SaveSettingsAsync(bool skipWhenExiting = false)
    {
        await _settingsSaveGate.WaitAsync();
        try
        {
            if (skipWhenExiting && InteractionFrozen)
            {
                return;
            }

            _settings = new PetSettings(Left, Top, _scale, _paused, Topmost)
            {
                DialogueEnabled = DialogueMenuItem.IsChecked,
                Tuning = _developerParameters.ToTuning(_voice is { Enabled: true })
            };
            await _saveSettingsAsync(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _dialogueWarmupViewState = DialogueWarmupViewState.Closed;
        _dialogueWarmupGeneration++;
        _pendingDialogueWarmupOutcome = null;
        _dialogueWarmupLifetime.Cancel();
        _dialogueWarmupLifetime.Dispose();
        _observedDialogueWarmup = null;
        ContentRendered -= Window_ContentRendered;
        _ambientTimer.Tick -= AmbientTimer_Tick;
        InvalidateAmbientSchedule();
        CancelActiveAmbientAction();
        _animation.Dispose();
        DisarmAutomaticTimer();
        _bubbleCountdown.Close();
        _bubbleTimer.Stop();
        StopVoicePlayback();
        _voice?.Dispose();
        _dialogueComposer?.CloseForShutdown();
        _personaDialogue?.Dispose();
        _memoryTimer.Stop();
        _eventTimer.Stop();
        if (!_suppressApplicationShutdownOnClose && !_shutdownRequested)
        {
            _shutdownRequested = true;
            _shutdownApplication();
        }
    }

    private enum DialogueWarmupViewState
    {
        Pending,
        Loading,
        RetryAvailable,
        Retrying,
        Ready,
        Closed
    }
}

internal readonly record struct MainWindowRuntimeSnapshot(
    bool IsPaused,
    bool IsMemoryTimerEnabled,
    bool IsAutomaticTimerEnabled,
    bool IsEventTimerEnabled,
    TimeSpan EventTimerInterval,
    bool IsAmbientTimerEnabled,
    bool IsBubbleTimerEnabled,
    BubbleCountdownState BubbleCountdownState,
    bool IsAnimationSuspended,
    PetActionState ActionState,
    long DialogueReplyRevision);
