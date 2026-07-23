using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CompanionDesktopPet.UI;

public sealed class AnimationController(
    ScaleTransform breathingScale,
    RotateTransform swayRotation,
    TranslateTransform floatingOffset,
    ScaleTransform reactionScale,
    RotateTransform reactionRotation,
    ScaleTransform actionScale,
    RotateTransform actionRotation,
    TranslateTransform actionOffset,
    IReadOnlyList<FrameworkElement> hearts,
    FrameworkElement? blinkOverlay = null,
    FrameworkElement? greetingBadge = null,
    TranslateTransform? greetingBadgeOffset = null)
{
    private readonly List<AnimationClock> _idleClocks = [];
    private readonly FrameworkElement _blinkOverlay = blinkOverlay ?? new FrameworkElement();
    private readonly FrameworkElement _greetingBadge = greetingBadge ?? new FrameworkElement();
    private readonly TranslateTransform _greetingBadgeOffset = greetingBadgeOffset ?? new TranslateTransform();
    private bool _started;
    private int _ambientAnimationVersion;

    public bool IsPaused { get; private set; }

    public void StartIdle()
    {
        foreach (var clock in _idleClocks)
        {
            clock.Controller?.Remove();
        }

        _idleClocks.Clear();
        ApplyIdle(breathingScale, ScaleTransform.ScaleXProperty, 1.0, 1.015, 2.0);
        ApplyIdle(breathingScale, ScaleTransform.ScaleYProperty, 0.985, 1.015, 2.0);
        ApplyIdle(swayRotation, RotateTransform.AngleProperty, -1.2, 1.2, 3.0);
        ApplyIdle(floatingOffset, TranslateTransform.YProperty, 3.0, -3.0, 2.5);
        _started = true;
        IsPaused = false;
    }

    public void PauseIdle()
    {
        foreach (var clock in _idleClocks)
        {
            clock.Controller?.Pause();
        }

        IsPaused = true;
    }

    public void ResumeIdle()
    {
        if (!_started)
        {
            StartIdle();
            return;
        }

        foreach (var clock in _idleClocks)
        {
            clock.Controller?.Resume();
        }

        IsPaused = false;
    }

    public void PlayClickReaction()
    {
        ApplyReaction(reactionScale, ScaleTransform.ScaleXProperty, 1.0, 1.06);
        ApplyReaction(reactionScale, ScaleTransform.ScaleYProperty, 1.0, 0.94);
        ApplyReaction(reactionRotation, RotateTransform.AngleProperty, 0.0, 2.2);
        PlayHearts();
    }

    public void SetDragLean(double horizontalDelta)
    {
        actionRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        actionRotation.Angle = Math.Clamp(horizontalDelta * 0.12, -8, 8);
    }

    public void PlayLanding(Action? completed = null)
    {
        actionRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        var initialAngle = actionRotation.Angle;
        CancelAmbientAction();
        var version = _ambientAnimationVersion;

        BeginFrames(
            actionRotation,
            RotateTransform.AngleProperty,
            520,
            (0, initialAngle),
            (170, -initialAngle * 0.35),
            (335, initialAngle * 0.14),
            (520, 0));
        var landingOffset = CreateFrames(
            520,
            false,
            (0, -2),
            (150, 6),
            (320, -2.5),
            (520, 0));
        landingOffset.Completed += (_, _) => CompleteAction(version, completed);
        actionOffset.BeginAnimation(
            TranslateTransform.YProperty,
            landingOffset,
            HandoffBehavior.SnapshotAndReplace);
        BeginFrames(
            actionScale,
            ScaleTransform.ScaleYProperty,
            520,
            (0, 1),
            (150, 0.965),
            (320, 1.018),
            (520, 1));
    }

    public void PlayBlink(bool doubleBlink, Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        CancelAmbientAction();
        var version = _ambientAnimationVersion;
        var frames = doubleBlink
            ? new (int Milliseconds, double Value)[]
            {
                (0, 0), (95, 1), (150, 1), (300, 0),
                (420, 0), (515, 1), (570, 1), (720, 0)
            }
            : new (int Milliseconds, double Value)[]
            {
                (0, 0), (95, 1), (150, 1), (300, 0)
            };
        var blink = CreateFrames(frames[^1].Milliseconds, false, frames);
        blink.Completed += (_, _) => CompleteBlink(version, completed);
        _blinkOverlay.BeginAnimation(
            UIElement.OpacityProperty,
            blink,
            HandoffBehavior.SnapshotAndReplace);
    }

    public void PlayGreeting(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        CancelAmbientAction();
        var version = _ambientAnimationVersion;

        BeginFrames(
            actionRotation,
            RotateTransform.AngleProperty,
            1100,
            (0, 0),
            (360, -3.0),
            (760, -1.0),
            (1100, 0));
        var greetingOffset = CreateFrames(
            1100,
            false,
            (0, 0),
            (360, -4.0),
            (760, -2.0),
            (1100, 0));
        greetingOffset.Completed += (_, _) => CompleteAction(version, completed);
        actionOffset.BeginAnimation(
            TranslateTransform.YProperty,
            greetingOffset,
            HandoffBehavior.SnapshotAndReplace);
        BeginFrames(
            actionScale,
            ScaleTransform.ScaleYProperty,
            1100,
            (0, 1),
            (360, 0.988),
            (760, 1.006),
            (1100, 1));
        BeginFrames(
            _greetingBadge,
            UIElement.OpacityProperty,
            900,
            (0, 0),
            (180, 1),
            (580, 1),
            (900, 0));
        BeginFrames(
            _greetingBadgeOffset,
            TranslateTransform.YProperty,
            900,
            (0, 8),
            (180, 0),
            (900, -20));
    }

    public void CancelAmbientAction()
    {
        _ambientAnimationVersion++;
        _blinkOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        _greetingBadge.BeginAnimation(UIElement.OpacityProperty, null);
        _greetingBadgeOffset.BeginAnimation(TranslateTransform.XProperty, null);
        _greetingBadgeOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _blinkOverlay.Opacity = 0;
        _greetingBadge.Opacity = 0;
        _greetingBadgeOffset.X = 0;
        _greetingBadgeOffset.Y = 8;
        ResetActionBase();
    }

    private void CompleteBlink(int version, Action completed)
    {
        if (version != _ambientAnimationVersion)
        {
            return;
        }

        _ambientAnimationVersion++;
        _blinkOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        _blinkOverlay.Opacity = 0;
        completed();
    }

    private void CompleteAction(int version, Action? completed)
    {
        if (version != _ambientAnimationVersion)
        {
            return;
        }

        _ambientAnimationVersion++;
        ResetActionBase();
        _greetingBadge.BeginAnimation(UIElement.OpacityProperty, null);
        _greetingBadgeOffset.BeginAnimation(TranslateTransform.XProperty, null);
        _greetingBadgeOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _greetingBadge.Opacity = 0;
        _greetingBadgeOffset.X = 0;
        _greetingBadgeOffset.Y = 8;
        completed?.Invoke();
    }

    private void PlayHearts()
    {
        for (var index = 0; index < hearts.Count; index++)
        {
            var heart = hearts[index];
            heart.BeginAnimation(UIElement.OpacityProperty, null);
            heart.Opacity = 0;
            var translate = heart.RenderTransform as TranslateTransform;
            if (translate is null)
            {
                translate = new TranslateTransform();
                heart.RenderTransform = translate;
            }

            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.Y = 0;
            var delay = TimeSpan.FromMilliseconds(index * 55);
            heart.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                {
                    BeginTime = delay,
                    AutoReverse = true,
                    Duration = TimeSpan.FromMilliseconds(310),
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                },
                HandoffBehavior.SnapshotAndReplace);
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(8, -45 - (index * 7), TimeSpan.FromMilliseconds(620))
                {
                    BeginTime = delay,
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                },
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void ResetActionBase()
    {
        actionScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        actionScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        actionRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        actionOffset.BeginAnimation(TranslateTransform.XProperty, null);
        actionOffset.BeginAnimation(TranslateTransform.YProperty, null);
        actionScale.ScaleX = 1;
        actionScale.ScaleY = 1;
        actionRotation.Angle = 0;
        actionOffset.X = 0;
        actionOffset.Y = 0;
    }

    private void ApplyIdle(
        Animatable target,
        DependencyProperty property,
        double from,
        double to,
        double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        var clock = animation.CreateClock();
        target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        _idleClocks.Add(clock);
    }

    private static void ApplyReaction(
        Animatable target,
        DependencyProperty property,
        double from,
        double to)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(110))
        {
            AutoReverse = true,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void BeginFrames(
        IAnimatable target,
        DependencyProperty property,
        int durationMilliseconds,
        params (int Milliseconds, double Value)[] frames) =>
        BeginFrames(target, property, durationMilliseconds, frames, false);

    private static void BeginFrames(
        IAnimatable target,
        DependencyProperty property,
        int durationMilliseconds,
        (int Milliseconds, double Value)[] frames,
        bool discrete)
    {
        target.BeginAnimation(
            property,
            CreateFrames(durationMilliseconds, discrete, frames),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames CreateFrames(
        int durationMilliseconds,
        bool discrete,
        params (int Milliseconds, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            FillBehavior = FillBehavior.Stop
        };

        foreach (var (milliseconds, value) in frames)
        {
            DoubleKeyFrame frame = discrete
                ? new DiscreteDoubleKeyFrame()
                : new EasingDoubleKeyFrame
                {
                    EasingFunction = new BackEase
                    {
                        Amplitude = 0.22,
                        EasingMode = EasingMode.EaseOut
                    }
                };
            frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds));
            frame.Value = value;
            animation.KeyFrames.Add(frame);
        }

        return animation;
    }

}
