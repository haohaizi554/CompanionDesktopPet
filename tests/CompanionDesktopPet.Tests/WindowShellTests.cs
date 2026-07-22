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
            var window = new MainWindow(PetSettings.Default, new SettingsService());

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
            window.Close();
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
