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
    IReadOnlyList<FrameworkElement> hearts)
{
    private readonly List<AnimationClock> _idleClocks = [];
    private bool _started;

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

    public void PlayLanding()
    {
        actionRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        var initialAngle = actionRotation.Angle;
        ResetActionBase();

        BeginFrames(
            actionRotation,
            RotateTransform.AngleProperty,
            520,
            (0, initialAngle),
            (170, -initialAngle * 0.35),
            (335, initialAngle * 0.14),
            (520, 0));
        BeginFrames(
            actionOffset,
            TranslateTransform.YProperty,
            520,
            (0, -2),
            (150, 6),
            (320, -2.5),
            (520, 0));
        BeginFrames(
            actionScale,
            ScaleTransform.ScaleYProperty,
            520,
            (0, 1),
            (150, 0.965),
            (320, 1.018),
            (520, 1));
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
        Animatable target,
        DependencyProperty property,
        int durationMilliseconds,
        params (int Milliseconds, double Value)[] frames) =>
        BeginFrames(target, property, durationMilliseconds, frames, false);

    private static void BeginFrames(
        Animatable target,
        DependencyProperty property,
        int durationMilliseconds,
        (int Milliseconds, double Value)[] frames,
        bool discrete)
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

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

}
