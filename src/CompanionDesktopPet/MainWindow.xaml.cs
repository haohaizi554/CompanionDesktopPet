using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
    private System.Windows.Point _mouseDown;
    private ScreenPoint _dragGrabOffset;
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
            _dialogueReplyRevision,
            _dialogue.CreateSnapshot().TurnCount);

    private bool InteractionFrozen => _exitCommandRunning || _isClosed;
    private bool PresentationSuspended => InteractionFrozen || _isHiddenToTray;

    internal MainWindow(MainWindowDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        InitializeComponent();
        _settings = dependencies.Settings;
        _settingsService = dependencies.SettingsService;
        _saveAgentMemoryAsync = dependencies.SaveAgentMemoryAsync
            ?? (dependencies.AgentMemoryService is null
                ? null
                : dependencies.AgentMemoryService.SaveAsync);
        _saveSettingsAsync = dependencies.SaveSettingsAsync ?? _settingsService.SaveAsync;
        _idleTimeProvider = dependencies.IdleTimeProvider ?? new WindowsIdleTimeProvider();
        _ambientScheduler = dependencies.AmbientScheduler ?? new AmbientActionScheduler();
        _autoStartService = dependencies.AutoStartService
            ?? (dependencies.SuppressApplicationShutdownOnClose
                ? DisabledAutoStartService.Instance
                : new WindowsAutoStartService());
        _timeProvider = dependencies.TimeProvider ?? TimeProvider.System;
        _suppressApplicationShutdownOnClose = dependencies.SuppressApplicationShutdownOnClose;
        _shutdownApplication = dependencies.ShutdownApplication
            ?? (() => System.Windows.Application.Current?.Shutdown());
        _dialogue = dependencies.DialogueService
            ?? DialogueService.CreateDeferred(
                dependencies.AgentMemory,
                timeProvider: _timeProvider);
        _dialogueWarmup = dependencies.WarmupCoordinator
            ?? new DialogueWarmupCoordinator(_dialogue, _timeProvider);
        _announceLiveRegionChanged = dependencies.AnnounceLiveRegionChanged
            ?? RaiseLiveRegionChanged;
        _foregroundFullscreenDetector = dependencies.ForegroundFullscreenDetector
            ?? new WindowsForegroundFullscreenDetector();
        _automaticCadence = new AutomaticDialogueCadenceController(
            dependencies.DialogueScheduler ?? new DialogueScheduler(_random),
            _timeProvider);
        _animation = dependencies.AnimationController ?? new AnimationController(
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
        PreviewKeyDown += CharacterStage_PreviewKeyDown;
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
        ControlMenu.Closed += ControlMenu_Closed;
        ControlMenu.PreviewKeyDown += ControlMenu_PreviewKeyDown;
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

    public MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService = null,
        AgentMemorySnapshot? agentMemory = null,
        IIdleTimeProvider? idleTimeProvider = null,
        bool suppressApplicationShutdownOnClose = false,
        Action? shutdownApplication = null)
        : this(new MainWindowDependencies(settings, settingsService)
        {
            AgentMemoryService = agentMemoryService,
            AgentMemory = agentMemory,
            IdleTimeProvider = idleTimeProvider,
            SuppressApplicationShutdownOnClose = suppressApplicationShutdownOnClose,
            ShutdownApplication = shutdownApplication
        })
    {
    }

    internal MainWindow(
        PetSettings settings,
        SettingsService settingsService,
        AgentMemoryService? agentMemoryService,
        AgentMemorySnapshot? agentMemory,
        IAutoStartService autoStartService)
        : this(new MainWindowDependencies(settings, settingsService)
        {
            AgentMemoryService = agentMemoryService,
            AgentMemory = agentMemory,
            AutoStartService = autoStartService
        })
    {
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

        ShowEventBubble(CompanionEvent.DragReleased, LocalNow, ObserveFullscreen());
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

    internal void ShowBubble(string text)
    {
        if (InteractionFrozen)
        {
            return;
        }

        SpeechText.Text = text;
        AutomationProperties.SetName(SpeechText, $"佳怡说：{text}");
        SpeechBubble.Visibility = Visibility.Visible;
        _bubbleCountdown.Show();
        if (_isHiddenToTray)
        {
            _bubbleSuspendedForWindowHide = true;
            BubblePopup.IsOpen = false;
            _bubbleTimer.Stop();
            return;
        }

        BubblePopup.IsOpen = true;
        _bubbleSuspendedForWindowHide = false;
        SpeechBubble.UpdateLayout();
        PositionBubble();
        Dispatcher.BeginInvoke(PositionBubble, DispatcherPriority.Loaded);
        _announceLiveRegionChanged(SpeechText);
        SynchronizeBubbleTimer();
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
        AutomationProperties.SetName(SpeechText, string.Empty);
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
        // Popup does not reliably move its native HWND when a RelativePoint target's
        // parent window moves and the relative offsets remain unchanged. Absolute
        // coordinates change with the character and keep the bubble physically anchored.
        BubblePopup.HorizontalOffset = placement.Origin.X - BubbleShadowSafety;
        BubblePopup.VerticalOffset = placement.Origin.Y - BubbleShadowSafety;
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

        _automaticTimer.Interval = _automaticCadence.Arm(
            localTime,
            fullscreen.EffectiveQuietMode);
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
            ShowBubble(reply.Text);
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
        if (resumingFromTray)
        {
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
        ControlMenu.Placement = PlacementMode.Center;
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
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ControlMenu.IsOpen)
                {
                    SayMenuItem.Focus();
                }
            },
            DispatcherPriority.Input);
    }

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
    long DialogueReplyRevision,
    int DialogueTurnCount);
