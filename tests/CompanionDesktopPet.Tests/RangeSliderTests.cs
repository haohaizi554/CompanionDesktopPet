using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class RangeSliderTests
{
    [Fact]
    public void SnapClampsToTheBandAndKeepsTheMaximumReachable()
    {
        Assert.Equal(1, RangeSliderMath.Fit(1.4, 1, 30, 1, 1));
        Assert.Equal(2, RangeSliderMath.Fit(1.6, 1, 30, 1, 1));
        Assert.Equal(2, RangeSliderMath.Fit(2, 0.5, 2, 0.5, 1));
        Assert.Equal(1.2, RangeSliderMath.Fit(1.35, 0.2, 1.5, 0.2, 1), 6);
        Assert.Equal(1.2, RangeSliderMath.Fit(1.24, 0.2, 1.5, 0.2, 0.1), 6);
        Assert.Equal(1.4, RangeSliderMath.Fit(1.35, 1, 2, 1, 0.1), 6);
        Assert.Equal(0.5, RangeSliderMath.Fit(0.54, 0.5, 2, 0.5, 0.1), 6);
        Assert.Equal(1, RangeSliderMath.Fit(1, 0.5, 2, 0.5, 0.1), 6);
        Assert.Equal(2, RangeSliderMath.Fit(2, 0.5, 2, 0.5, 0.1), 6);
        Assert.Equal(1.25, RangeSliderMath.Fit(1.25, 0, 3, 0, 0));
        Assert.Equal(4, RangeSliderMath.Fit(double.NaN, 4, 4, 4, 1));
        Assert.Equal(4, RangeSliderMath.Fit(9, 4, 4, 4, 1));
    }

    [Fact]
    public void LowerAndUpperCannotCrossOrBreakTheMinimumGap()
    {
        Assert.Equal(5, RangeSliderMath.FitLower(20, 1, 30, 5, 1, 0, true));
        Assert.Equal(5, RangeSliderMath.FitUpper(1, 1, 30, 5, 1, 0));
        Assert.Equal(8, RangeSliderMath.FitLower(8, 1, 30, 8, 1, 0, true));
        Assert.Equal(8, RangeSliderMath.FitUpper(8, 1, 30, 8, 1, 0));
        Assert.Equal(12, RangeSliderMath.FitLower(14, 1, 30, 15, 1, 3, true));
        Assert.Equal(17, RangeSliderMath.FitUpper(15, 1, 30, 14, 1, 3));
        Assert.Equal(30, RangeSliderMath.FitLower(40, 1, 30, 30, 1, 0, false));
        Assert.Equal(1, RangeSliderMath.FitLower(15, 1, 30, 10, 1, 100, true));
        Assert.Equal(30, RangeSliderMath.FitUpper(15, 1, 30, 20, 1, 100));
    }

    [Fact]
    public void PositionMappingRoundTripsAndSurvivesACollapsedTrack()
    {
        var center = RangeSliderMath.ValueToCenter(15, 1, 30, 220, RangeSlider.HitSize);
        var value = RangeSliderMath.CenterToValue(center, 1, 30, 220, RangeSlider.HitSize);

        Assert.Equal(15, value, 6);
        Assert.Equal(RangeSlider.HitSize / 2, RangeSliderMath.ValueToCenter(10, 1, 30, 0, RangeSlider.HitSize));
        Assert.Equal(5, RangeSliderMath.CenterToValue(4, 5, 5, 180, RangeSlider.HitSize));
        Assert.True(center > RangeSliderMath.ValueToCenter(5, 1, 30, 220, RangeSlider.HitSize));
    }

    [Fact]
    public void ControlPreservesThumbIdentityWhenValuesArePushedFromOutside()
    {
        RunOnStaThread(() =>
        {
            var slider = new RangeSlider();
            slider.SetBounds(1, 30, 5, 15, 1);
            Assert.Equal(5, slider.LowerValue);
            Assert.Equal(15, slider.UpperValue);

            slider.LowerValue = 40;
            Assert.Equal(15, slider.LowerValue);
            Assert.Equal(15, slider.UpperValue);

            slider.SetBounds(1, 30, 5, 15, 1);
            slider.UpperValue = 2;
            Assert.Equal(5, slider.LowerValue);
            Assert.Equal(5, slider.UpperValue);

            slider.MinimumRange = 4;
            slider.SetBounds(1, 30, 10, 12, 1);
            Assert.True(slider.UpperValue - slider.LowerValue >= 4);

            slider.IsRange = false;
            slider.SetBounds(50, 200, 80.4, 999, 1);
            Assert.Equal(80, slider.LowerValue);
            Assert.Equal(80, slider.UpperValue);

            slider.IsEnabled = false;
            slider.LowerValue = 120;
            Assert.Equal(120, slider.LowerValue);

            Layout(slider, 36);
            Layout(slider, 640);
        });
    }

    [Fact]
    public void RenderedThumbsSitOnTheSelectedSpan()
    {
        RunOnStaThread(() =>
        {
            var root = new System.Windows.Controls.StackPanel
            {
                Width = 220,
                Background = new SolidColorBrush(Color.FromRgb(255, 244, 247))
            };
            root.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/CompanionDesktopPet;component/Themes/PetTheme.xaml", UriKind.Relative)
            });

            var wide = new RangeSlider { Width = 220 };
            wide.SetBounds(1, 30, 5, 15, 1);
            var crossed = new RangeSlider { Width = 220 };
            crossed.SetBounds(30, 180, 60, 120, 1);
            var single = new RangeSlider { Width = 220, IsRange = false };
            single.SetBounds(50, 200, 98, 98, 1);
            root.Children.Add(wide);
            root.Children.Add(crossed);
            root.Children.Add(single);
            Layout(root, 220);

            var pixels = new RenderTargetBitmap(220, 96, 96, 96, PixelFormats.Pbgra32);
            pixels.Render(root);

            var pink = 0;
            var white = 0;
            var stride = 220 * 4;
            var buffer = new byte[stride * 96];
            pixels.CopyPixels(buffer, stride, 0);
            for (var i = 0; i < buffer.Length; i += 4)
            {
                var blue = buffer[i];
                var green = buffer[i + 1];
                var red = buffer[i + 2];
                if (red > 220 && green < 160 && blue > 120 && blue < 190)
                {
                    pink++;
                }

                if (red > 245 && green > 245 && blue > 245)
                {
                    white++;
                }
            }

            Assert.True(pink > 40, "选中轨道没有画出来。");
            Assert.True(white > 20, "拇指白边没有画出来。");
        });
    }

    private static void Layout(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, 120));
        element.Arrange(new Rect(0, 0, width, 120));
        element.UpdateLayout();
    }

    private static readonly StaGate Gate = new();

    private static void RunOnStaThread(Action action) => Gate.Invoke(action);

    private sealed class StaGate
    {
        private Dispatcher _dispatcher = null!;

        public StaGate()
        {
            using var ready = new ManualResetEventSlim();
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/CompanionDesktopPet;component/Themes/PetTheme.xaml", UriKind.Relative)
                    });
                    _dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    ready.Set();
                }

                if (failure is null)
                {
                    Dispatcher.Run();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait();
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        public void Invoke(Action action) => _dispatcher.Invoke(action);
    }
}
