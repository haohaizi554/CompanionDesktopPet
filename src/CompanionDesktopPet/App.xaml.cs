using System.Windows;
using System.Windows.Threading;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _instanceGuard;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceGuard = new SingleInstanceGuard("Local\\CompanionDesktopPet-7E5D78F4");
        if (!_instanceGuard.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += HandleDispatcherException;
        var settingsService = new SettingsService();
        var settings = await settingsService.LoadAsync();
        var window = new MainWindow(settings, settingsService);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= HandleDispatcherException;
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "桌宠遇到问题，需要先休息一下。\n\n" + e.Exception.Message,
            "角色桌宠",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
