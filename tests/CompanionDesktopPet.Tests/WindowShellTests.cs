using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

public sealed class WindowShellTests
{
    [Fact]
    public void MainWindow_UsesTransparentDesktopPetChrome()
    {
        RunOnStaThread(() =>
        {
            var app = System.Windows.Application.Current as App ?? new App();
            app.InitializeComponent();
            var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(PetSettings.Default, new SettingsService(settingsDirectory));

            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(window.AllowsTransparency);
            Assert.Null(window.Background);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            Assert.Equal("角色桌宠", window.Title);
            Assert.NotNull(window.FindName("SpeechBubble"));
            var image = Assert.IsType<Image>(window.FindName("PetImage"));
            Assert.NotNull(image.Source);
            Assert.NotNull(image.ContextMenu);
            Assert.True(image.ContextMenu.Items.Count >= 8);

            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bubble = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("SpeechBubble"));
            var speech = Assert.IsType<TextBlock>(window.FindName("SpeechText"));
            Assert.Equal(Visibility.Visible, bubble.Visibility);
            Assert.False(string.IsNullOrWhiteSpace(speech.Text));

            var say = Assert.IsType<MenuItem>(image.ContextMenu.Items[0]);
            say.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(string.IsNullOrWhiteSpace(speech.Text));

            var pause = Assert.IsType<MenuItem>(image.ContextMenu.Items[1]);
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("继续动画", pause.Header);

            var size = Assert.IsType<MenuItem>(image.ContextMenu.Items[3]);
            var large = Assert.IsType<MenuItem>(size.Items[2]);
            large.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(390, image.Width);

            var topmost = Assert.IsType<MenuItem>(image.ContextMenu.Items[4]);
            topmost.IsChecked = false;
            topmost.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(window.Topmost);
            window.Close();
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
