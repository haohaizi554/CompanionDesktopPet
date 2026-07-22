using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CompanionDesktopPet.UI;

public sealed class AnimationController(
    ScaleTransform breathingScale,
    RotateTransform swayRotation,
    TranslateTransform floatingOffset,
    ScaleTransform reactionScale,
    RotateTransform reactionRotation)
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
}
