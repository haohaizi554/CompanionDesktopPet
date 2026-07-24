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
    private readonly AgentMemoryService? _agentMemoryService;
    private readonly AnimationController _animation;
    private readonly DispatcherTimer _automaticTimer = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _memoryTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _eventTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _ambientTimer = new();
    private readonly BubbleCountdownController _bubbleCountdown = new();
    private readonly PetActionCoordinator _actionCoordinator = new();
    private readonly AmbientActionScheduler _ambientScheduler;
    private readonly IIdleTimeProvider _idleTimeProvider;
    private readonly bool _suppressApplicationShutdownOnClose;
    private readonly Action _shutdownApplication;
    private CompanionEventPump? _eventPump;
    private PetSettings _settings;
    private PetScale _scale;
    private bool _paused;
    private bool _dragged;
    private bool _shutdownRequested;
    private StartupGreetingState _startupGreetingState = StartupGreetingState.Pending;
    private bool _runningSmokeProbe;
    private bool _isClosed;
    private PetAmbientAction _pendingAmbientAction;
    private long _ambientScheduleGeneration;
    private long _armedAmbientGeneration;
    private long _ambientDueTimestamp;
    private System.Windows.Point _mouseDown;
    private double _lastDragLeft;

    internal AgentReply? LastReply { get; private set; }

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
            new AmbientActionScheduler())
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
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        _agentMemoryService = agentMemoryService;
        _idleTimeProvider = idleTimeProvider ?? new WindowsIdleTimeProvider();
        _ambientScheduler = ambientScheduler
            ?? throw new ArgumentNullException(nameof(ambientScheduler));
        _suppressApplicationShutdownOnClose = suppressApplicationShutdownOnClose;
        _shutdownApplication = shutdownApplication
            ?? (() => System.Windows.Application.Current?.Shutdown());
        _dialogue = new DialogueService(agentMemory);
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
        Closed += Window_Closed;
        PetImage.PreviewMouseLeftButtonDown += PetImage_MouseLeftButtonDown;
        PetImage.PreviewMouseMove += PetImage_MouseMove;
        PetImage.PreviewMouseLeftButtonUp += PetImage_MouseLeftButtonUp;
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
        RestorePositionMenuItem.Click += RestorePosition_Click;
        ExitMenuItem.Click += Exit_Click;
        _bubbleTimer.Tick += BubbleTimer_Tick;
        _automaticTimer.Tick += AutomaticTimer_Tick;
        _memoryTimer.Tick += MemoryTimer_Tick;
        _eventTimer.Tick += EventTimer_Tick;
        _ambientTimer.Tick += AmbientTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
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
        _eventPump = new CompanionEventPump(DateTime.Now, _idleTimeProvider.GetIdleTime());
        _eventTimer.Start();
        ScheduleNextPhrase();
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        ScheduleNextAmbientAction();
    }

    private void AmbientTimer_Tick(object? sender, EventArgs e)
    {
        if (_isClosed)
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
        if (_isClosed
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
        if (_isClosed
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

    private void PlaceOnScreen()
    {
        var workAreas = WorkAreaService.GetWorkAreas();
        if (workAreas.Count == 0)
        {
            var work = SystemParameters.WorkArea;
            workAreas = [new ScreenRect(work.Left, work.Top, work.Width, work.Height)];
        }

        var requested = double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top)
            ? DefaultPosition(workAreas[0])
            : new ScreenPoint(_settings.Left, _settings.Top);
        var clamped = ScreenPlacementService.Clamp(
            requested,
            ActualWidth,
            ActualHeight,
            workAreas);
        Left = clamped.X;
        Top = clamped.Y;
    }

    private ScreenPoint DefaultPosition(ScreenRect workArea) =>
        new(workArea.Right - ActualWidth - 24, workArea.Bottom - ActualHeight - 24);

    private void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = e.GetPosition(this);
        _dragged = false;
        PetImage.CaptureMouse();
        e.Handled = true;
    }

    private async void PetImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _mouseDown.X) <= 4 && Math.Abs(current.Y - _mouseDown.Y) <= 4)
        {
            return;
        }

        _dragged = true;
        PetImage.ReleaseMouseCapture();
        BeginDragAction();
        _lastDragLeft = Left;
        LocationChanged += Window_LocationChanged;
        try
        {
            DragMove();
        }
        finally
        {
            LocationChanged -= Window_LocationChanged;
            BeginLandingAction();
        }

        if (_isClosed)
        {
            return;
        }

        await CompleteDragAfterMoveAsync();
    }

    private async Task CompleteDragAfterMoveAsync()
    {
        if (_isClosed)
        {
            return;
        }

        ShowEventBubble(CompanionEvent.DragReleased);
        ScheduleNextPhrase();
        if (_isClosed)
        {
            return;
        }

        await SaveSettingsAsync();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        var horizontalDelta = Left - _lastDragLeft;
        _lastDragLeft = Left;
        _animation.SetDragLean(horizontalDelta);
    }

    private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PetImage.ReleaseMouseCapture();
        if (!_dragged)
        {
            ReactAndSpeak();
        }

        _dragged = false;
        e.Handled = true;
    }

    private void ReactAndSpeak()
    {
        _animation.PlayClickReaction();
        ShowEventBubble(CompanionEvent.Click);
        ScheduleNextPhrase();
    }

    private void BeginDragAction()
    {
        PreserveScheduledStartupGreeting();
        InvalidateAmbientSchedule();
        CancelActiveAmbientAction();
        _actionCoordinator.BeginDrag();
    }

    private void BeginLandingAction()
    {
        if (_isClosed)
        {
            return;
        }

        _actionCoordinator.BeginLanding();
        _animation.PlayLanding(() =>
        {
            _actionCoordinator.Complete(PetActionState.Landing);
            if (!_isClosed && _actionCoordinator.State == PetActionState.Idle)
            {
                ScheduleNextAmbientAction();
            }
        });
    }

    private void ShowBubble(string text)
    {
        SpeechText.Text = text;
        SpeechBubble.Visibility = Visibility.Visible;
        _bubbleCountdown.Show();
        SynchronizeBubbleTimer();
    }

    private void BubbleHover_MouseEnter(object sender, MouseEventArgs? e)
    {
        _bubbleCountdown.Enter(sender == SpeechBubble
            ? BubbleHoverTarget.Bubble
            : BubbleHoverTarget.Character);
        SynchronizeBubbleTimer();
    }

    private void BubbleHover_MouseLeave(object sender, MouseEventArgs? e)
    {
        _bubbleCountdown.Leave(sender == SpeechBubble
            ? BubbleHoverTarget.Bubble
            : BubbleHoverTarget.Character);
        SynchronizeBubbleTimer();
    }

    private void BubbleTimer_Tick(object? sender, EventArgs e)
    {
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
    }

    private void SynchronizeBubbleTimer()
    {
        _bubbleTimer.Stop();
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
        _automaticTimer.Interval = _scheduler.NextDelay(DateTime.Now);
        _automaticTimer.Start();
    }

    private void AutomaticTimer_Tick(object? sender, EventArgs e)
    {
        ShowEventBubble(CompanionEvent.Automatic);
        ScheduleNextPhrase();
    }

    private void EventTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
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
        var reply = _dialogue.GetReply(trigger, DateTime.Now, _random);
        LastReply = reply;
        PresentReply(reply);

        if (_agentMemoryService is not null)
        {
            _memoryTimer.Stop();
            _memoryTimer.Start();
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
        await SaveAgentMemoryAsync();
    }

    private void SaySomething_Click(object sender, RoutedEventArgs e) => ReactAndSpeak();

    private void Greeting_Click(object sender, RoutedEventArgs e)
    {
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
        await SaveSettingsAsync();
    }

    private void UpdatePauseLabel() =>
        PauseMenuItem.Header = _paused ? "继续动画" : "暂停动画";

    private async void SetSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }
            || !Enum.TryParse(tag, out PetScale scale))
        {
            return;
        }

        _scale = scale;
        ApplyScale(scale);
        PlaceOnScreen();
        ShowEventBubble(CompanionEvent.SizeChanged);
        await SaveSettingsAsync();
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
        Topmost = TopmostMenuItem.IsChecked;
        await SaveSettingsAsync();
    }

    private async void RestorePosition_Click(object sender, RoutedEventArgs e)
    {
        var workAreas = WorkAreaService.GetWorkAreas();
        var work = workAreas.Count > 0
            ? workAreas[0]
            : new ScreenRect(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);
        var point = DefaultPosition(work);
        Left = point.X;
        Top = point.Y;
        ShowEventBubble(CompanionEvent.PositionRestored);
        await SaveSettingsAsync();
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        await SaveSettingsAsync();
        await SaveAgentMemoryAsync();
        Close();
    }

    private async Task SaveAgentMemoryAsync()
    {
        if (_agentMemoryService is null)
        {
            return;
        }

        try
        {
            await _agentMemoryService.SaveAsync(_dialogue.CreateSnapshot());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task SaveSettingsAsync()
    {
        _settings = new PetSettings(Left, Top, _scale, _paused, Topmost);
        try
        {
            await _settingsService.SaveAsync(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
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
