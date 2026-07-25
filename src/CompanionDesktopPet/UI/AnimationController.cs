using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CompanionDesktopPet.UI;

public enum ClickSide
{
    Left,
    Right
}

internal interface IPetAnimationController : IDisposable
{
    void StartIdle();
    void PauseIdle();
    void ResumeIdle();
    void PlayClickReaction();
    void PlayClickReaction(ClickSide clickSide);
    void SetDragLean(double horizontalDelta);
    void PlayLanding(Action? completed);
    void PlayBlink(bool doubleBlink, Action completed);
    void PlayGreeting(Action completed);
    void CancelAmbientAction();
    void Suspend();
    void Resume();
}

public sealed class AnimationController : IPetAnimationController
{
    private readonly ScaleTransform breathingScale;
    private readonly RotateTransform swayRotation;
    private readonly TranslateTransform floatingOffset;
    private readonly ScaleTransform reactionScale;
    private readonly RotateTransform reactionRotation;
    private readonly ScaleTransform actionScale;
    private readonly RotateTransform actionRotation;
    private readonly TranslateTransform actionOffset;
    private readonly IReadOnlyList<FrameworkElement> hearts;
    private readonly List<AppliedAnimation> _activeAnimations = [];
    private readonly FrameworkElement _blinkOverlay;
    private readonly FrameworkElement _greetingBadge;
    private readonly TranslateTransform _greetingBadgeOffset;
    private bool _started;
    private bool _disposed;
    private int _ambientAnimationVersion;

    public AnimationController(
        ScaleTransform breathingScale,
        RotateTransform swayRotation,
        TranslateTransform floatingOffset,
        ScaleTransform reactionScale,
        RotateTransform reactionRotation,
        ScaleTransform actionScale,
        RotateTransform actionRotation,
        TranslateTransform actionOffset,
        IReadOnlyList<FrameworkElement> hearts)
        : this(
            breathingScale,
            swayRotation,
            floatingOffset,
            reactionScale,
            reactionRotation,
            actionScale,
            actionRotation,
            actionOffset,
            hearts,
            new FrameworkElement(),
            new FrameworkElement(),
            new TranslateTransform())
    {
    }

    public AnimationController(
        ScaleTransform breathingScale,
        RotateTransform swayRotation,
        TranslateTransform floatingOffset,
        ScaleTransform reactionScale,
        RotateTransform reactionRotation,
        ScaleTransform actionScale,
        RotateTransform actionRotation,
        TranslateTransform actionOffset,
        IReadOnlyList<FrameworkElement> hearts,
        FrameworkElement blinkOverlay,
        FrameworkElement greetingBadge,
        TranslateTransform greetingBadgeOffset)
    {
        this.breathingScale = breathingScale ?? throw new ArgumentNullException(nameof(breathingScale));
        this.swayRotation = swayRotation ?? throw new ArgumentNullException(nameof(swayRotation));
        this.floatingOffset = floatingOffset ?? throw new ArgumentNullException(nameof(floatingOffset));
        this.reactionScale = reactionScale ?? throw new ArgumentNullException(nameof(reactionScale));
        this.reactionRotation = reactionRotation ?? throw new ArgumentNullException(nameof(reactionRotation));
        this.actionScale = actionScale ?? throw new ArgumentNullException(nameof(actionScale));
        this.actionRotation = actionRotation ?? throw new ArgumentNullException(nameof(actionRotation));
        this.actionOffset = actionOffset ?? throw new ArgumentNullException(nameof(actionOffset));
        this.hearts = hearts ?? throw new ArgumentNullException(nameof(hearts));
        _blinkOverlay = blinkOverlay ?? throw new ArgumentNullException(nameof(blinkOverlay));
        _greetingBadge = greetingBadge ?? throw new ArgumentNullException(nameof(greetingBadge));
        _greetingBadgeOffset = greetingBadgeOffset ?? throw new ArgumentNullException(nameof(greetingBadgeOffset));
    }

    public bool IsPaused { get; private set; }
    public bool IsSuspended { get; private set; }
    internal int ActiveClockCount => _activeAnimations.Count;
    internal IReadOnlyList<AnimationClock> ActiveClocks =>
        _activeAnimations.Select(animation => animation.Clock).ToArray();

    public void StartIdle()
    {
        if (_disposed)
        {
            return;
        }

        RemoveIdleAnimations();
        ApplyIdle(breathingScale, ScaleTransform.ScaleXProperty, 1.0, 1.015, 2.0);
        ApplyIdle(breathingScale, ScaleTransform.ScaleYProperty, 0.985, 1.015, 2.0);
        ApplyIdle(swayRotation, RotateTransform.AngleProperty, -1.2, 1.2, 3.0);
        ApplyIdle(floatingOffset, TranslateTransform.YProperty, 3.0, -3.0, 2.5);
        _started = true;
        IsPaused = false;
    }

    public void PauseIdle()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var animation in _activeAnimations.Where(animation => animation.IsIdle))
        {
            animation.Clock.Controller?.Pause();
        }

        IsPaused = true;
    }

    public void ResumeIdle()
    {
        if (_disposed)
        {
            return;
        }

        if (!_started)
        {
            StartIdle();
            return;
        }

        foreach (var animation in _activeAnimations.Where(animation => animation.IsIdle))
        {
            if (!IsSuspended)
            {
                animation.Clock.Controller?.Resume();
            }
        }

        IsPaused = false;
    }

    public void PlayClickReaction() => PlayClickReaction(ClickSide.Left);

    public void PlayClickReaction(ClickSide clickSide)
    {
        if (_disposed)
        {
            return;
        }

        ApplyReaction(reactionScale, ScaleTransform.ScaleXProperty, 1.0, 1.06);
        ApplyReaction(reactionScale, ScaleTransform.ScaleYProperty, 1.0, 0.94);
        var targetAngle = clickSide switch
        {
            ClickSide.Left => 2.2,
            ClickSide.Right => -2.2,
            _ => throw new ArgumentOutOfRangeException(nameof(clickSide))
        };
        RemoveAnimation(reactionRotation, RotateTransform.AngleProperty);
        reactionRotation.Angle = 0;
        ApplyReaction(reactionRotation, RotateTransform.AngleProperty, 0.0, targetAngle);
        PlayHearts();
    }

    public void SetDragLean(double horizontalDelta)
    {
        if (_disposed)
        {
            return;
        }

        RemoveAnimation(actionRotation, RotateTransform.AngleProperty);
        actionRotation.Angle = Math.Clamp(horizontalDelta * 0.12, -8, 8);
    }

    public void PlayLanding() => PlayLanding(null);

    public void PlayLanding(Action? completed)
    {
        if (_disposed)
        {
            return;
        }

        RemoveAnimation(actionRotation, RotateTransform.AngleProperty);
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
        ApplyAnimation(
            actionOffset,
            TranslateTransform.YProperty,
            landingOffset,
            isIdle: false,
            () => CompleteAction(version, completed));
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
        if (_disposed)
        {
            return;
        }

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
        var blink = CreateBoundedFrames(frames[^1].Milliseconds, frames);
        ApplyAnimation(
            _blinkOverlay,
            UIElement.OpacityProperty,
            blink,
            isIdle: false,
            () => CompleteBlink(version, completed));
    }

    public void PlayGreeting(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (_disposed)
        {
            return;
        }

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
        ApplyAnimation(
            actionOffset,
            TranslateTransform.YProperty,
            greetingOffset,
            isIdle: false,
            () => CompleteAction(version, completed));
        BeginBoundedFrames(
            actionScale,
            ScaleTransform.ScaleYProperty,
            1100,
            (0, 1),
            (360, 0.988),
            (760, 1.006),
            (1100, 1));
        BeginBoundedFrames(
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
        if (_disposed)
        {
            return;
        }

        CancelAmbientActionCore();
    }

    private void CancelAmbientActionCore()
    {
        _ambientAnimationVersion++;
        RemoveAnimation(_blinkOverlay, UIElement.OpacityProperty);
        RemoveAnimation(_greetingBadge, UIElement.OpacityProperty);
        RemoveAnimation(_greetingBadgeOffset, TranslateTransform.XProperty);
        RemoveAnimation(_greetingBadgeOffset, TranslateTransform.YProperty);
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
        RemoveAnimation(_blinkOverlay, UIElement.OpacityProperty);
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
        RemoveAnimation(_greetingBadge, UIElement.OpacityProperty);
        RemoveAnimation(_greetingBadgeOffset, TranslateTransform.XProperty);
        RemoveAnimation(_greetingBadgeOffset, TranslateTransform.YProperty);
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
            RemoveAnimation(heart, UIElement.OpacityProperty);
            heart.Opacity = 0;
            var translate = heart.RenderTransform as TranslateTransform;
            if (translate is null)
            {
                translate = new TranslateTransform();
                heart.RenderTransform = translate;
            }

            RemoveAnimation(translate, TranslateTransform.YProperty);
            translate.Y = 0;
            var delay = TimeSpan.FromMilliseconds(index * 55);
            ApplyAnimation(
                heart,
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                {
                    BeginTime = delay,
                    AutoReverse = true,
                    Duration = TimeSpan.FromMilliseconds(310),
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                },
                isIdle: false);
            ApplyAnimation(
                translate,
                TranslateTransform.YProperty,
                new DoubleAnimation(8, -45 - (index * 7), TimeSpan.FromMilliseconds(620))
                {
                    BeginTime = delay,
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                },
                isIdle: false);
        }
    }

    private void ResetActionBase()
    {
        RemoveAnimation(actionScale, ScaleTransform.ScaleXProperty);
        RemoveAnimation(actionScale, ScaleTransform.ScaleYProperty);
        RemoveAnimation(actionRotation, RotateTransform.AngleProperty);
        RemoveAnimation(actionOffset, TranslateTransform.XProperty);
        RemoveAnimation(actionOffset, TranslateTransform.YProperty);
        actionScale.ScaleX = 1;
        actionScale.ScaleY = 1;
        actionRotation.Angle = 0;
        actionOffset.X = 0;
        actionOffset.Y = 0;
    }

    public void Suspend()
    {
        if (_disposed || IsSuspended)
        {
            return;
        }

        IsSuspended = true;
        foreach (var animation in _activeAnimations)
        {
            animation.Clock.Controller?.Pause();
        }
    }

    public void Resume()
    {
        if (_disposed || !IsSuspended)
        {
            return;
        }

        IsSuspended = false;
        foreach (var animation in _activeAnimations)
        {
            if (!animation.IsIdle || !IsPaused)
            {
                animation.Clock.Controller?.Resume();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ambientAnimationVersion++;
        foreach (var animation in _activeAnimations.ToArray())
        {
            RemoveAnimation(animation);
        }

        _started = false;
        IsPaused = true;
        IsSuspended = false;
        _blinkOverlay.Opacity = 0;
        _greetingBadge.Opacity = 0;
        _greetingBadgeOffset.X = 0;
        _greetingBadgeOffset.Y = 8;
        actionScale.ScaleX = 1;
        actionScale.ScaleY = 1;
        actionRotation.Angle = 0;
        actionOffset.X = 0;
        actionOffset.Y = 0;
        reactionScale.ScaleX = 1;
        reactionScale.ScaleY = 1;
        reactionRotation.Angle = 0;
        foreach (var heart in hearts)
        {
            heart.Opacity = 0;
            if (heart.RenderTransform is TranslateTransform translate)
            {
                translate.Y = 0;
            }
        }
    }

    private void ApplyAnimation(
        IAnimatable target,
        DependencyProperty property,
        AnimationTimeline animation,
        bool isIdle,
        Action? completed = null)
    {
        if (_disposed)
        {
            return;
        }

        RemoveAnimation(target, property);
        var clock = (AnimationClock)animation.CreateClock(true);
        var applied = new AppliedAnimation(target, property, clock, isIdle);
        if (!isIdle)
        {
            applied.CompletedHandler = (_, _) =>
            {
                if (!RemoveAnimation(applied) || _disposed)
                {
                    return;
                }

                completed?.Invoke();
            };
            clock.Completed += applied.CompletedHandler;
        }

        _activeAnimations.Add(applied);
        try
        {
            target.ApplyAnimationClock(
                property,
                clock,
                HandoffBehavior.SnapshotAndReplace);
            if (IsSuspended || (isIdle && IsPaused))
            {
                clock.Controller?.Pause();
            }
        }
        catch
        {
            RemoveAnimation(applied);
            throw;
        }
    }

    private void RemoveIdleAnimations()
    {
        foreach (var animation in _activeAnimations
                     .Where(animation => animation.IsIdle)
                     .ToArray())
        {
            RemoveAnimation(animation);
        }
    }

    private void RemoveAnimation(IAnimatable target, DependencyProperty property)
    {
        var animation = _activeAnimations.LastOrDefault(candidate =>
            ReferenceEquals(candidate.Target, target)
            && candidate.Property == property);
        if (animation is not null)
        {
            RemoveAnimation(animation);
            return;
        }

        target.BeginAnimation(property, null);
    }

    private bool RemoveAnimation(AppliedAnimation animation)
    {
        if (!_activeAnimations.Remove(animation))
        {
            return false;
        }

        if (animation.CompletedHandler is not null)
        {
            animation.Clock.Completed -= animation.CompletedHandler;
        }

        animation.Clock.Controller?.Remove();
        animation.Target.BeginAnimation(animation.Property, null);
        return true;
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
        ApplyAnimation(target, property, animation, isIdle: true);
    }

    private void ApplyReaction(
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
        ApplyAnimation(target, property, animation, isIdle: false);
    }

    private void BeginFrames(
        IAnimatable target,
        DependencyProperty property,
        int durationMilliseconds,
        params (int Milliseconds, double Value)[] frames) =>
        BeginFrames(target, property, durationMilliseconds, frames, false);

    private void BeginFrames(
        IAnimatable target,
        DependencyProperty property,
        int durationMilliseconds,
        (int Milliseconds, double Value)[] frames,
        bool discrete)
    {
        ApplyAnimation(
            target,
            property,
            CreateFrames(durationMilliseconds, discrete, frames),
            isIdle: false);
    }

    private void BeginBoundedFrames(
        IAnimatable target,
        DependencyProperty property,
        int durationMilliseconds,
        params (int Milliseconds, double Value)[] frames)
    {
        ApplyAnimation(
            target,
            property,
            CreateBoundedFrames(durationMilliseconds, frames),
            isIdle: false);
    }

    private static DoubleAnimationUsingKeyFrames CreateBoundedFrames(
        int durationMilliseconds,
        params (int Milliseconds, double Value)[] frames) =>
        CreateFrames(durationMilliseconds, false, frames, allowOvershoot: false);

    private static DoubleAnimationUsingKeyFrames CreateFrames(
        int durationMilliseconds,
        bool discrete,
        params (int Milliseconds, double Value)[] frames) =>
        CreateFrames(durationMilliseconds, discrete, frames, allowOvershoot: true);

    private static DoubleAnimationUsingKeyFrames CreateFrames(
        int durationMilliseconds,
        bool discrete,
        (int Milliseconds, double Value)[] frames,
        bool allowOvershoot)
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
                    EasingFunction = allowOvershoot
                        ? new BackEase
                        {
                            Amplitude = 0.22,
                            EasingMode = EasingMode.EaseOut
                        }
                        : new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
            frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds));
            frame.Value = value;
            animation.KeyFrames.Add(frame);
        }

        return animation;
    }

    private sealed class AppliedAnimation(
        IAnimatable target,
        DependencyProperty property,
        AnimationClock clock,
        bool isIdle)
    {
        public IAnimatable Target { get; } = target;
        public DependencyProperty Property { get; } = property;
        public AnimationClock Clock { get; } = clock;
        public bool IsIdle { get; } = isIdle;
        public EventHandler? CompletedHandler { get; set; }
    }

}
