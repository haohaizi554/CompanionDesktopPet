using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet.Tests;

public sealed class PetThemeManagerTests
{
    [Fact]
    public void HighContrastChanges_ApplySystemPaletteAndRestoreNormalTheme()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary();
            var provider = new FakeHighContrastProvider();
            using var manager = new PetThemeManager(
                resources,
                Dispatcher.CurrentDispatcher,
                provider);

            Assert.False(resources.Contains("Pet.Theme.HighContrast"));

            provider.SetHighContrast(true);

            Assert.Equal(true, resources["Pet.Theme.HighContrast"]);
            Assert.Same(SystemColors.WindowBrush, resources["Pet.Brush.BubbleSurface"]);
            Assert.Same(SystemColors.WindowTextBrush, resources["Pet.Brush.TextPrimary"]);
            Assert.Same(SystemColors.HighlightBrush, resources["Pet.Brush.AccentBorder"]);
            Assert.Equal(Colors.Transparent, resources["Pet.Color.Shadow.Bubble"]);
            Assert.Equal(0d, resources["Pet.Opacity.Shadow.Bubble"]);
            Assert.Equal(1d, resources["Pet.Opacity.Disabled"]);

            provider.SetHighContrast(false);

            Assert.False(resources.Contains("Pet.Theme.HighContrast"));
            Assert.False(resources.Contains("Pet.Brush.BubbleSurface"));
            Assert.False(resources.Contains("Pet.Color.Shadow.Bubble"));
        });
    }

    [Fact]
    public void Dispose_UnsubscribesFromFutureThemeChanges()
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary();
            var provider = new FakeHighContrastProvider();
            var manager = new PetThemeManager(
                resources,
                Dispatcher.CurrentDispatcher,
                provider);

            manager.Dispose();
            provider.SetHighContrast(true);

            Assert.False(resources.Contains("Pet.Theme.HighContrast"));
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class FakeHighContrastProvider : IHighContrastProvider
    {
        public bool IsHighContrast { get; private set; }

        public event EventHandler? Changed;

        public void SetHighContrast(bool value)
        {
            IsHighContrast = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
