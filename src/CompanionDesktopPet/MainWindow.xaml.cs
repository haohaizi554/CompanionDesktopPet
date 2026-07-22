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
    private readonly DialogueService _dialogue = new();
    private readonly Random _random = new();
    private readonly DialogueScheduler _scheduler;
    private readonly SettingsService _settingsService;
    private readonly AnimationController _animation;
    private readonly DispatcherTimer _automaticTimer = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private PetSettings _settings;
    private PetScale _scale;
    private bool _paused;
    private bool _dragged;
    private System.Windows.Point _mouseDown;

    public MainWindow(PetSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _settings = settings;
        _settingsService = settingsService;
        _scheduler = new DialogueScheduler(_random);
        _animation = new AnimationController(
            BreathingScale,
            SwayRotation,
            FloatingOffset,
            ReactionScale,
            ReactionRotation);

        Loaded += Window_Loaded;
        Closed += Window_Closed;
        PetImage.PreviewMouseLeftButtonDown += PetImage_MouseLeftButtonDown;
        PetImage.PreviewMouseMove += PetImage_MouseMove;
        PetImage.PreviewMouseLeftButtonUp += PetImage_MouseLeftButtonUp;
        SayMenuItem.Click += SaySomething_Click;
        PauseMenuItem.Click += ToggleAnimation_Click;
        SmallSizeMenuItem.Click += SetSize_Click;
        NormalSizeMenuItem.Click += SetSize_Click;
        LargeSizeMenuItem.Click += SetSize_Click;
        TopmostMenuItem.Click += ToggleTopmost_Click;
        RestorePositionMenuItem.Click += RestorePosition_Click;
        ExitMenuItem.Click += Exit_Click;
        _bubbleTimer.Tick += BubbleTimer_Tick;
        _automaticTimer.Tick += AutomaticTimer_Tick;
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
        }

        UpdatePauseLabel();
        ShowBubble(_dialogue.GetGreeting(DateTime.Now));
        ScheduleNextPhrase();
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
        DragMove();
        await SaveSettingsAsync();
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
        ShowBubble(_dialogue.GetNextPhrase(_random));
        ScheduleNextPhrase();
    }

    private void ShowBubble(string text)
    {
        SpeechText.Text = text;
        SpeechBubble.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    private void BubbleTimer_Tick(object? sender, EventArgs e)
    {
        _bubbleTimer.Stop();
        SpeechBubble.Visibility = Visibility.Collapsed;
    }

    private void ScheduleNextPhrase()
    {
        _automaticTimer.Stop();
        _automaticTimer.Interval = _scheduler.NextDelay();
        _automaticTimer.Start();
    }

    private void AutomaticTimer_Tick(object? sender, EventArgs e)
    {
        ShowBubble(_dialogue.GetNextPhrase(_random));
        ScheduleNextPhrase();
    }

    private void SaySomething_Click(object sender, RoutedEventArgs e) => ReactAndSpeak();

    private async void ToggleAnimation_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_paused)
        {
            _animation.PauseIdle();
        }
        else
        {
            _animation.ResumeIdle();
        }

        UpdatePauseLabel();
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
        await SaveSettingsAsync();
    }

    private void ApplyScale(PetScale scale)
    {
        PetImage.Width = scale switch
        {
            PetScale.Small => 250,
            PetScale.Large => 390,
            _ => 320
        };
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
        await SaveSettingsAsync();
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        await SaveSettingsAsync();
        System.Windows.Application.Current.Shutdown();
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
        _automaticTimer.Stop();
        _bubbleTimer.Stop();
    }
}
