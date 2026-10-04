using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CompanionDesktopPet.UI;

public partial class RangeSlider : UserControl
{
    public const double HitSize = 28;
    public const double TrackInset = 9;

    private enum ActiveThumb
    {
        None,
        Lower,
        Upper
    }

    private bool _suspendCoerce;
    private ActiveThumb _lastActive = ActiveThumb.Lower;

    public RangeSlider()
    {
        InitializeComponent();
        TrackHit.MouseLeftButtonDown += Track_MouseLeftButtonDown;
        Activate(ActiveThumb.Lower);
        foreach (var thumb in new[] { LowerThumb, UpperThumb })
        {
            thumb.MouseEnter += Thumb_MouseEnter;
            thumb.MouseLeave += Thumb_MouseLeave;
        }

        Loaded += (_, _) => UpdateVisuals();
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(0d, OnRangePropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(1d, OnRangePropertyChanged));

    public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
        nameof(LowerValue),
        typeof(double),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnRangePropertyChanged,
            CoerceLower));

    public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
        nameof(UpperValue),
        typeof(double),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(
            1d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnRangePropertyChanged,
            CoerceUpper));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step),
        typeof(double),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(1d, OnRangePropertyChanged));

    public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
        nameof(MinimumRange),
        typeof(double),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(0d, OnRangePropertyChanged));

    public static readonly DependencyProperty IsRangeProperty = DependencyProperty.Register(
        nameof(IsRange),
        typeof(bool),
        typeof(RangeSlider),
        new FrameworkPropertyMetadata(true, OnRangePropertyChanged));

    public event EventHandler? RangeChanged;

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double LowerValue
    {
        get => (double)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public double UpperValue
    {
        get => (double)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double MinimumRange
    {
        get => (double)GetValue(MinimumRangeProperty);
        set => SetValue(MinimumRangeProperty, value);
    }

    public bool IsRange
    {
        get => (bool)GetValue(IsRangeProperty);
        set => SetValue(IsRangeProperty, value);
    }

    public void SetBounds(double minimum, double maximum, double lower, double upper, double step)
    {
        _suspendCoerce = true;
        try
        {
            Step = step;
            Minimum = minimum;
            Maximum = maximum < minimum ? minimum : maximum;
            LowerValue = lower;
            UpperValue = upper;
        }
        finally
        {
            _suspendCoerce = false;
        }

        CoerceValue(LowerValueProperty);
        CoerceValue(UpperValueProperty);
        UpdateVisuals();
        RangeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnRangePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not RangeSlider slider || slider._suspendCoerce)
        {
            return;
        }

        slider.CoerceValue(LowerValueProperty);
        slider.CoerceValue(UpperValueProperty);
        slider.UpdateVisuals();
        slider.RangeChanged?.Invoke(slider, EventArgs.Empty);
    }

    private static object CoerceLower(DependencyObject sender, object baseValue)
    {
        var slider = (RangeSlider)sender;
        if (slider._suspendCoerce || baseValue is not double value)
        {
            return baseValue;
        }

        return RangeSliderMath.FitLower(
            value,
            slider.Minimum,
            slider.Maximum,
            slider.UpperValue,
            slider.Step,
            slider.MinimumRange,
            slider.IsRange);
    }

    private static object CoerceUpper(DependencyObject sender, object baseValue)
    {
        var slider = (RangeSlider)sender;
        if (slider._suspendCoerce || baseValue is not double value)
        {
            return baseValue;
        }

        if (!slider.IsRange)
        {
            return RangeSliderMath.Fit(slider.LowerValue, slider.Minimum, slider.Maximum, slider.Minimum, slider.Step);
        }

        return RangeSliderMath.FitUpper(
            value,
            slider.Minimum,
            slider.Maximum,
            slider.LowerValue,
            slider.Step,
            slider.MinimumRange);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => UpdateVisuals();

    private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        Opacity = IsEnabled ? 1 : 0.46;
        Cursor = IsEnabled ? Cursors.Arrow : Cursors.Arrow;
    }

    private void Track_MouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (!IsEnabled)
        {
            return;
        }

        var value = RangeSliderMath.CenterToValue(args.GetPosition(Host).X, Minimum, Maximum, Host.ActualWidth, HitSize);
        if (!IsRange)
        {
            Activate(ActiveThumb.Lower);
            LowerValue = value;
        }
        else
        {
            var lowerDistance = Math.Abs(value - LowerValue);
            var upperDistance = Math.Abs(value - UpperValue);
            var moveLower = lowerDistance < upperDistance
                || (lowerDistance == upperDistance && _lastActive != ActiveThumb.Upper);
            Activate(moveLower ? ActiveThumb.Lower : ActiveThumb.Upper);
            if (moveLower)
            {
                LowerValue = value;
            }
            else
            {
                UpperValue = value;
            }
        }

        args.Handled = true;
    }

    private void Thumb_MouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is Thumb thumb && !thumb.IsDragging)
        {
            Animate(thumb, 1.08);
        }
    }

    private void Thumb_MouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is Thumb thumb && !thumb.IsDragging)
        {
            Animate(thumb, 1);
        }
    }

    private void Thumb_DragStarted(object sender, DragStartedEventArgs args)
    {
        Activate(ReferenceEquals(sender, LowerThumb) ? ActiveThumb.Lower : ActiveThumb.Upper);
        Animate((Thumb)sender, 0.97);
    }

    private void Thumb_DragDelta(object sender, DragDeltaEventArgs args)
    {
        var lower = ReferenceEquals(sender, LowerThumb);
        var value = RangeSliderMath.CenterToValue(Mouse.GetPosition(Host).X, Minimum, Maximum, Host.ActualWidth, HitSize);
        if (lower)
        {
            LowerValue = value;
        }
        else
        {
            UpperValue = value;
        }
    }

    private void Thumb_DragCompleted(object sender, DragCompletedEventArgs args)
    {
        var thumb = (Thumb)sender;
        Animate(thumb, thumb.IsMouseOver ? 1.08 : 1);
    }

    private void Thumb_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        var lower = ReferenceEquals(sender, LowerThumb);
        var current = lower ? LowerValue : UpperValue;
        var step = Step > 0 ? Step : 1;
        double? next = args.Key switch
        {
            Key.Left or Key.Down => current - step,
            Key.Right or Key.Up => current + step,
            Key.Home => lower || !IsRange ? Minimum : LowerValue + Math.Max(0, MinimumRange),
            Key.End => !lower && IsRange ? Maximum : IsRange ? UpperValue - Math.Max(0, MinimumRange) : Maximum,
            _ => null
        };
        if (next is null)
        {
            return;
        }

        Activate(lower ? ActiveThumb.Lower : ActiveThumb.Upper);
        if (lower || !IsRange)
        {
            LowerValue = next.Value;
        }
        else
        {
            UpperValue = next.Value;
        }

        args.Handled = true;
    }

    private void Activate(ActiveThumb thumb)
    {
        _lastActive = thumb;
        Panel.SetZIndex(LowerThumb, thumb == ActiveThumb.Lower ? 2 : 1);
        Panel.SetZIndex(UpperThumb, thumb == ActiveThumb.Upper ? 2 : 1);
    }

    private void UpdateVisuals()
    {
        if (Host is null || Track is null || Selected is null || LowerThumb is null || UpperThumb is null)
        {
            return;
        }

        var width = Host.ActualWidth;
        UpperThumb.Visibility = IsRange ? Visibility.Visible : Visibility.Collapsed;
        UpperThumb.Focusable = IsRange;
        var trackWidth = Math.Max(0, width - TrackInset * 2);
        Track.Width = trackWidth;
        TrackHit.Width = trackWidth;
        Canvas.SetLeft(Track, TrackInset);
        Canvas.SetLeft(TrackHit, TrackInset);
        Place(LowerThumb, RangeSliderMath.ValueToCenter(LowerValue, Minimum, Maximum, width, HitSize));
        if (IsRange)
        {
            var upperCenter = RangeSliderMath.ValueToCenter(UpperValue, Minimum, Maximum, width, HitSize);
            var lowerCenter = Canvas.GetLeft(LowerThumb) + HitSize / 2;
            Place(UpperThumb, upperCenter);
            Canvas.SetLeft(Selected, Math.Min(lowerCenter, upperCenter));
            Selected.Width = Math.Max(0, Math.Abs(upperCenter - lowerCenter));
        }
        else
        {
            var center = Canvas.GetLeft(LowerThumb) + HitSize / 2;
            Canvas.SetLeft(Selected, TrackInset);
            Selected.Width = Math.Max(0, center - TrackInset);
        }
    }

    private static void Place(Thumb thumb, double center)
    {
        Canvas.SetLeft(thumb, center - HitSize / 2);
        Canvas.SetTop(thumb, 2);
    }

    private static void Animate(UIElement element, double scale)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(120);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
    }
}

internal static class RangeSliderMath
{
    public static double Fit(double value, double low, double high, double origin, double step)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = low;
        }

        if (high < low)
        {
            high = low;
        }

        value = Math.Clamp(value, low, high);
        if (step <= 0 || high <= low)
        {
            return value;
        }

        var snapped = origin + Math.Round((value - origin) / step, MidpointRounding.AwayFromZero) * step;
        if (step < 1)
        {
            var scale = Math.Round(1d / step);
            if (scale > 1)
            {
                snapped = Math.Round(snapped * scale, MidpointRounding.AwayFromZero) / scale;
            }
        }

        return Math.Clamp(snapped, low, high);
    }

    public static double FitLower(
        double value,
        double minimum,
        double maximum,
        double upper,
        double step,
        double gap,
        bool isRange)
    {
        var high = isRange ? Math.Min(maximum, upper - Math.Max(0, gap)) : maximum;
        if (high < minimum)
        {
            high = minimum;
        }

        return Fit(value, minimum, high, minimum, step);
    }

    public static double FitUpper(
        double value,
        double minimum,
        double maximum,
        double lower,
        double step,
        double gap)
    {
        var low = Math.Max(minimum, lower + Math.Max(0, gap));
        if (low > maximum)
        {
            low = maximum;
        }

        return Fit(value, low, maximum, minimum, step);
    }

    public static double ValueToCenter(double value, double minimum, double maximum, double width, double hit)
    {
        var usable = Math.Max(0, width - hit);
        if (usable <= 0 || maximum <= minimum)
        {
            return hit / 2;
        }

        var ratio = Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        return hit / 2 + ratio * usable;
    }

    public static double CenterToValue(double center, double minimum, double maximum, double width, double hit)
    {
        var usable = Math.Max(0, width - hit);
        if (usable <= 0 || maximum <= minimum)
        {
            return minimum;
        }

        var ratio = Math.Clamp((center - hit / 2) / usable, 0, 1);
        return minimum + ratio * (maximum - minimum);
    }
}
