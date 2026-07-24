using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet;

public partial class MainWindow : Window
{
    private readonly DialogueService _dialogue;
    private readonly Random _random = new();
    private readonly DialogueScheduler _scheduler;
    private readonly SettingsService _settingsService;
    private readonly Func<AgentMemorySnapshot, Task>? _saveAgentMemoryAsync;
    private readonly Func<PetSettings, Task> _saveSettingsAsync;
    private readonly AnimationController _animation;
    private readonly DispatcherTimer _automaticTimer = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
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
    private readonly CancellationTokenSource _dialogueWarmupLifetime = new();
    private readonly bool _suppressApplicationShutdownOnClose;
    private readonly Action _shutdownApplication;
    private const double BubbleShadowSafety = 10;
    private CompanionEventPump? _eventPump;
    private PetSettings _settings;
    private PetScale _scale;
    private bool _paused;
    private bool _dragged;
    private bool _shutdownRequested;
    private StartupGreetingState _startupGreetingState = StartupGreetingState.Pending;
    private bool _runningSmokeProbe;
    private bool _isClosed;
    private bool _lastKnownAutoStart;
    private bool _trayAvailable;
    private bool _exitCommandRunning;
    private PetAmbientAction _pendingAmbientAction;
    private long _ambientScheduleGeneration;
    private long _armedAmbientGeneration;
    private long _ambientDueTimestamp;
    private System.Windows.Point _mouseDown;
    private ScreenPoint _dragGrabOffset;
    private double _lastDragLeft;
    private bool _dragCompletionStarted;
    private BubblePlacementSide _bubbleSide = BubblePlacementSide.Above;
    private bool _bubbleSuspendedForWindowHide;
    private Task<DialogueWarmupOutcome>? _observedDialogueWarmup;
    private long _dialogueReplyRevision;
    private long _dialogueWarmupGeneration;
    private long _startupFallbackReplyRevision;
    private bool _replayStartupAfterWarmupRequested;

    internal AgentReply? LastReply { get; private set; }

    private bool InteractionFrozen => _exitCommandRunning || _isClosed;

    public MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService = null,
        AgentMemorySnapshot? agentMemory = null,
        IIdleTimeProvider? idleTimeProvider = null,
        bool suppressApplicationShutdownOnClose = false,
        Action? shutdownApplication = null)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider,
            suppressApplicationShutdownOnClose,
            shutdownApplication,
            new AmbientActionScheduler(),
            suppressApplicationShutdownOnClose
                ? DisabledAutoStartService.Instance
                : new WindowsAutoStartService())
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IAutoStartService autoStartService)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider: null,
            suppressApplicationShutdownOnClose: false,
            shutdownApplication: null,
            new AmbientActionScheduler(),
            autoStartService)
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IIdleTimeProvider? idleTimeProvider,
        bool suppressApplicationShutdownOnClose,
        Action? shutdownApplication,
        AmbientActionScheduler ambientScheduler)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider,
            suppressApplicationShutdownOnClose,
            shutdownApplication,
            ambientScheduler,
            suppressApplicationShutdownOnClose
                ? DisabledAutoStartService.Instance
                : new WindowsAutoStartService())
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IIdleTimeProvider? idleTimeProvider,
        bool suppressApplicationShutdownOnClose,
        Action? shutdownApplication,
        AmbientActionScheduler ambientScheduler,
        IAutoStartService autoStartService)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider,
            suppressApplicationShutdownOnClose,
            shutdownApplication,
            ambientScheduler,
            autoStartService,
            agentMemoryService is null ? null : agentMemoryService.SaveAsync)
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IIdleTimeProvider? idleTimeProvider,
        bool suppressApplicationShutdownOnClose,
        Action? shutdownApplication,
        AmbientActionScheduler ambientScheduler,
        IAutoStartService autoStartService,
        Func<AgentMemorySnapshot, Task>? saveAgentMemoryAsync)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider,
            suppressApplicationShutdownOnClose,
            shutdownApplication,
            ambientScheduler,
            autoStartService,
            saveAgentMemoryAsync,
            saveSettingsAsync: null)
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IIdleTimeProvider? idleTimeProvider,
        bool suppressApplicationShutdownOnClose,
        Action? shutdownApplication,
        AmbientActionScheduler ambientScheduler,
        IAutoStartService autoStartService,
        Func<AgentMemorySnapshot, Task>? saveAgentMemoryAsync,
        Func<PetSettings, Task>? saveSettingsAsync)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider,
            suppressApplicationShutdownOnClose,
            shutdownApplication,
            ambientScheduler,
            autoStartService,
            saveAgentMemoryAsync,
            saveSettingsAsync,
            dialogueService: null,
            timeProvider: null)
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IIdleTimeProvider? idleTimeProvider,
        bool suppressApplicationShutdownOnClose,
        Action? shutdownApplication,
        AmbientActionScheduler ambientScheduler,
        IAutoStartService autoStartService,
        Func<AgentMemorySnapshot, Task>? saveAgentMemoryAsync,
        Func<PetSettings, Task>? saveSettingsAsync,
        DialogueService? dialogueService,
        TimeProvider? timeProvider)
        : this(
            settings,
            settingsService,
            agentMemoryService,
            agentMemory,
            idleTimeProvider,
            suppressApplicationShutdownOnClose,
            shutdownApplication,
            ambientScheduler,
            autoStartService,
            saveAgentMemoryAsync,
            saveSettingsAsync,
            dialogueService,
            timeProvider,
            warmupCoordinator: null)
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IIdleTimeProvider? idleTimeProvider,
        bool suppressApplicationShutdownOnClose,
        Action? shutdownApplication,
        AmbientActionScheduler ambientScheduler,
        IAutoStartService autoStartService,
        Func<AgentMemorySnapshot, Task>? saveAgentMemoryAsync,
        Func<PetSettings, Task>? saveSettingsAsync,
        DialogueService? dialogueService,
        TimeProvider? timeProvider,
        DialogueWarmupCoordinator? warmupCoordinator)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService
            ?? throw new ArgumentNullException(nameof(settingsService));
        _saveAgentMemoryAsync = saveAgentMemoryAsync;
        _saveSettingsAsync = saveSettingsAsync ?? _settingsService.SaveAsync;
        _idleTimeProvider = idleTimeProvider ?? new WindowsIdleTimeProvider();
        _ambientScheduler = ambientScheduler
            ?? throw new ArgumentNullException(nameof(ambientScheduler));
        _autoStartService = autoStartService
            ?? throw new ArgumentNullException(nameof(autoStartService));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _suppressApplicationShutdownOnClose = suppressApplicationShutdownOnClose;
        _shutdownApplication = shutdownApplication
            ?? (() => System.Windows.Application.Current?.Shutdown());
        _dialogue = dialogueService
            ?? DialogueService.CreateDeferred(agentMemory, timeProvider: _timeProvider);
        _dialogueWarmup = warmupCoordinator
            ?? new DialogueWarmupCoordinator(_dialogue, _timeProvider);
        _scheduler = new DialogueScheduler(_random);
        _animation = new AnimationController(
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
        Closed += Window_Closed;
        PetImage.PreviewMouseLeftButtonDown += PetImage_MouseLeftButtonDown;
        PetImage.PreviewMouseMove += PetImage_MouseMove;
        PetImage.PreviewMouseLeftButtonUp += PetImage_MouseLeftButtonUp;
        PetImage.LostMouseCapture += PetImage_LostMouseCapture;
        CharacterStage.MouseEnter += BubbleHover_MouseEnter;
        CharacterStage.MouseLeave += BubbleHover_MouseLeave;
        SpeechBubble.MouseEnter += BubbleHover_MouseEnter;
        SpeechBubble.MouseLeave += BubbleHover_MouseLeave;
        SayMenuItem.Click += SaySomething_Click;
        GreetingMenuItem.Click += Greeting_Click;
        PauseMenuItem.Click += ToggleAnimation_Click;
        SmallSizeMenuItem.Click += SetSize_Click;
        NormalSizeMenuItem.Click += SetSize_Click;
        LargeSizeMenuItem.Click += SetSize_Click;
        TopmostMenuItem.Click += ToggleTopmost_Click;
        ControlMenu.Opened += ControlMenu_Opened;
        AutoStartMenuItem.Click += ToggleAutoStart_Click;
        RestorePositionMenuItem.Click += RestorePosition_Click;
        HideToTrayMenuItem.Click += HideToTray_Click;
        ExitMenuItem.Click += Exit_Click;
        UpdateTrayAvailabilityControls();
        _bubbleTimer.Tick += BubbleTimer_Tick;
        _automaticTimer.Tick += AutomaticTimer_Tick;
        _memoryTimer.Tick += MemoryTimer_Tick;
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
        ApplyScale(_scale);
        PlaceOnScreen();
        _animation.StartIdle();
        if (_paused)
        {
            _animation.PauseIdle();
            _actionCoordinator.Pause();
        }

        UpdatePauseLabel();
        ShowEventBubble(CompanionEvent.Startup);
        _startupFallbackReplyRevision = _dialogueReplyRevision;
        var now = LocalNow;
        _eventPump = new CompanionEventPump(now, _idleTimeProvider.GetIdleTime());
        _eventTimer.Start();
        ScheduleNextPhrase();
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
        {
            return;
        }

        ObserveDialogueWarmup(replayStartupWhenReady: true);
        ScheduleNextAmbientAction();
    }

    private void AmbientTimer_Tick(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
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
            && _startupGreetingState == StartupGreetingState.Scheduled;
        InvalidateAmbientSchedule();
        if (!_actionCoordinator.TryBeginAmbient(action))
        {
            if (isStartupGreeting)
            {
                _startupGreetingState = StartupGreetingState.Pending;
            }

            return;
        }

        if (isStartupGreeting)
        {
            _startupGreetingState = StartupGreetingState.Running;
        }

        PlayAmbientAction(action, isStartupGreeting);
    }

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
        if (_startupGreetingState == StartupGreetingState.Running)
        {
            _startupGreetingState = StartupGreetingState.Completed;
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
        if (InteractionFrozen
            || _runningSmokeProbe
            || _paused
            || _actionCoordinator.State != PetActionState.Idle)
        {
            return;
        }

        if (_startupGreetingState == StartupGreetingState.Pending)
        {
            ScheduleAmbientAction(
                PetAmbientAction.Greeting,
                TimeSpan.FromMilliseconds(650));
            _startupGreetingState = StartupGreetingState.Scheduled;
            return;
        }

        if (_startupGreetingState == StartupGreetingState.Completed)
        {
            ScheduleFreshBlink();
        }
    }

    private void ScheduleAmbientAction(PetAmbientAction action, TimeSpan delay)
    {
        InvalidateAmbientSchedule();
        if (InteractionFrozen
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
        if (_startupGreetingState == StartupGreetingState.Scheduled)
        {
            _startupGreetingState = StartupGreetingState.Pending;
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
        if (_startupGreetingState == StartupGreetingState.Running)
        {
            _startupGreetingState = StartupGreetingState.Completed;
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
            failure = "The full dialogue runtime is not ready.";
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
            ShowEventBubble(CompanionEvent.Startup);
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

        _mouseDown = e.GetPosition(this);
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
            && Math.Abs(current.X - _mouseDown.X) <= 4
            && Math.Abs(current.Y - _mouseDown.Y) <= 4)
        {
            return;
        }

        if (!_dragged)
        {
            _dragged = true;
            BeginDragAction();
            _lastDragLeft = Left;
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

    private async Task CompleteDragAfterMoveAsync()
    {
        if (InteractionFrozen)
        {
            return;
        }

        ShowEventBubble(CompanionEvent.DragReleased);
        ScheduleNextPhrase();
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
    }

    private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!InteractionFrozen && _dragged)
        {
            FinishDragOnce();
        }
        else if (!InteractionFrozen)
        {
            var clickPosition = e.GetPosition(PetImage);
            ReactAndSpeak(ResolveClickSide(clickPosition.X, PetImage.ActualWidth));
        }

        _dragged = false;
        PetImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PetImage_LostMouseCapture(object sender, MouseEventArgs e) =>
        FinishDragOnce();

    private void FinishDragOnce()
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

        if (clickSide is { } resolvedClickSide)
        {
            _animation.PlayClickReaction(resolvedClickSide);
        }
        else
        {
            _animation.PlayClickReaction();
        }

        ShowEventBubble(CompanionEvent.Click);
        ScheduleNextPhrase();
    }

    private void BeginDragAction()
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

    private void BeginLandingAction()
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

    private void ShowBubble(string text)
    {
        if (InteractionFrozen)
        {
            return;
        }

        SpeechText.Text = text;
        SpeechBubble.Visibility = Visibility.Visible;
        BubblePopup.IsOpen = true;
        _bubbleSuspendedForWindowHide = false;
        SpeechBubble.UpdateLayout();
        PositionBubble();
        Dispatcher.BeginInvoke(PositionBubble, DispatcherPriority.Loaded);
        _bubbleCountdown.Show();
        SynchronizeBubbleTimer();
    }

    private void BubbleHover_MouseEnter(object sender, MouseEventArgs? e)
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

    private void BubbleHover_MouseLeave(object sender, MouseEventArgs? e)
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

    private void BubbleTimer_Tick(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
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
        _bubbleTimer.Stop();
        SpeechText.Text = string.Empty;
        SpeechBubble.Visibility = Visibility.Collapsed;
        BubblePopup.IsOpen = false;
        _bubbleSuspendedForWindowHide = false;
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
        BubblePopup.HorizontalOffset = placement.Origin.X - character.Left - BubbleShadowSafety;
        BubblePopup.VerticalOffset = placement.Origin.Y - character.Top - BubbleShadowSafety;
    }

    private void SynchronizeBubbleTimer()
    {
        _bubbleTimer.Stop();
        if (InteractionFrozen)
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

    private void ScheduleNextPhrase()
    {
        _automaticTimer.Stop();
        if (InteractionFrozen)
        {
            return;
        }

        _automaticTimer.Interval = _scheduler.NextDelay(LocalNow);
        _automaticTimer.Start();
    }

    private void AutomaticTimer_Tick(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
        {
            _automaticTimer.Stop();
            return;
        }

        ShowEventBubble(CompanionEvent.Automatic);
        ScheduleNextPhrase();
    }

    private void EventTimer_Tick(object? sender, EventArgs e)
    {
        if (InteractionFrozen)
        {
            _eventTimer.Stop();
            return;
        }

        var now = LocalNow;
        _eventPump ??= new CompanionEventPump(now, _idleTimeProvider.GetIdleTime());
        var companionEvent = _eventPump.Poll(
            now,
            _idleTimeProvider.GetIdleTime(),
            _dialogue.NextStoryDueAt);
        if (companionEvent is { } trigger)
        {
            ShowEventBubble(trigger);
        }
    }

    private void ShowEventBubble(CompanionEvent trigger)
    {
        if (InteractionFrozen)
        {
            return;
        }

        if (!_dialogue.IsReady && trigger != CompanionEvent.Startup)
        {
            ObserveDialogueWarmup(replayStartupWhenReady: false);
        }

        var reply = _dialogue.GetReply(trigger, LocalNow, _random);
        LastReply = reply;
        _dialogueReplyRevision++;
        PresentReply(reply);

        if (_saveAgentMemoryAsync is not null && _dialogue.IsReady)
        {
            _memoryTimer.Stop();
            _memoryTimer.Start();
        }
    }

    private DateTime LocalNow => _timeProvider.GetLocalNow().LocalDateTime;

    private void ObserveDialogueWarmup(bool replayStartupWhenReady)
    {
        _replayStartupAfterWarmupRequested |= replayStartupWhenReady;
        if (InteractionFrozen || _dialogue.IsReady)
        {
            return;
        }

        var warmup = _dialogueWarmup.StartAsync(_dialogueWarmupLifetime.Token);
        if (ReferenceEquals(warmup, _observedDialogueWarmup))
        {
            return;
        }

        _observedDialogueWarmup = warmup;
        var generation = ++_dialogueWarmupGeneration;
        _ = CompleteDialogueWarmupAsync(warmup, generation);
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

        if (outcome != DialogueWarmupOutcome.Ready)
        {
            if (outcome is (DialogueWarmupOutcome.PermanentFailure
                    or DialogueWarmupOutcome.RetriesExhausted)
                && _dialogueWarmup.LastError is { } error)
            {
                Trace.TraceError("Dialogue warmup stopped after {0}: {1}", outcome, error);
            }

            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (InteractionFrozen || generation != _dialogueWarmupGeneration)
                {
                    return;
                }

                if (_replayStartupAfterWarmupRequested
                    && _startupFallbackReplyRevision == _dialogueReplyRevision)
                {
                    ShowEventBubble(CompanionEvent.Startup);
                }
            }, DispatcherPriority.Background);
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

    private void PresentReply(AgentReply reply)
    {
        if (reply.ShouldDisplayText)
        {
            ShowBubble(reply.Text);
        }
        else if (reply.Trigger == CompanionEvent.Click)
        {
            HideBubble();
        }
    }

    private async void MemoryTimer_Tick(object? sender, EventArgs e)
    {
        _memoryTimer.Stop();
        if (InteractionFrozen)
        {
            return;
        }

        await SaveAgentMemoryAsync(skipWhenExiting: true);
    }

    internal void SaySomething() => ReactAndSpeak();

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
        ShowEventBubble(_paused ? CompanionEvent.AnimationPaused : CompanionEvent.AnimationResumed);
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
        ShowEventBubble(CompanionEvent.SizeChanged);
        await SaveSettingsAsync(skipWhenExiting: true);
    }

    private void ApplyScale(PetScale scale)
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
        ShowEventBubble(CompanionEvent.PositionRestored);
        await SaveSettingsAsync(skipWhenExiting: true);
    }

    internal void HideToTray()
    {
        if (!InteractionFrozen && _trayAvailable)
        {
            _bubbleSuspendedForWindowHide = BubblePopup.IsOpen;
            BubblePopup.IsOpen = false;
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
        HideToTrayMenuItem.ToolTip = _trayAvailable
            ? null
            : "托盘暂时不可用，桌宠会保持显示。";
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
        Show();
        WindowState = WindowState.Normal;
        UpdateLayout();
        EnsureCurrentPositionIsVisible();
        if (_bubbleSuspendedForWindowHide
            && SpeechBubble.Visibility == Visibility.Visible)
        {
            BubblePopup.IsOpen = true;
        }

        _bubbleSuspendedForWindowHide = false;
        PositionBubble();
        Activate();
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
            ShowBubble("Windows 暂时不允许读取开机启动设置。");
            return;
        }

        SetKnownAutoStartState(current);
        ApplyAutoStart(!current);
    }

    private void ControlMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (!InteractionFrozen)
        {
            RefreshAutoStartState();
        }
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
    }

    private void MarkAutoStartUnavailable()
    {
        AutoStartMenuItem.IsChecked = _lastKnownAutoStart;
        AutoStartMenuItem.IsEnabled = false;
        AutoStartMenuItem.ToolTip = "Windows 暂时不允许读取开机启动设置。";
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
        ShowBubble("开机启动没设置上，Windows 不让改。");
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
        _automaticTimer.Stop();
        _eventTimer.Stop();
        _memoryTimer.Stop();
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

            _settings = new PetSettings(Left, Top, _scale, _paused, Topmost);
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
        _dialogueWarmupGeneration++;
        _dialogueWarmupLifetime.Cancel();
        _dialogueWarmupLifetime.Dispose();
        _observedDialogueWarmup = null;
        ContentRendered -= Window_ContentRendered;
        _ambientTimer.Tick -= AmbientTimer_Tick;
        InvalidateAmbientSchedule();
        CancelActiveAmbientAction();
        _automaticTimer.Stop();
        _bubbleCountdown.Close();
        _bubbleTimer.Stop();
        _memoryTimer.Stop();
        _eventTimer.Stop();
        if (!_suppressApplicationShutdownOnClose && !_shutdownRequested)
        {
            _shutdownRequested = true;
            _shutdownApplication();
        }
    }

    private enum StartupGreetingState
    {
        Pending,
        Scheduled,
        Running,
        Completed
    }
}
