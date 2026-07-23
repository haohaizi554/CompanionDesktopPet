using System.IO;
using System.Windows;
using System.Windows.Threading;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _instanceGuard;
    private bool _smokeTest;
    private string? _smokeDirectory;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _smokeTest = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        var instanceName = _smokeTest
            ? $"Local\\CompanionDesktopPet-Smoke-{Environment.ProcessId}-{Guid.NewGuid():N}"
            : "Local\\CompanionDesktopPet-7E5D78F4";
        _instanceGuard = new SingleInstanceGuard(instanceName);
        if (!_instanceGuard.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += HandleDispatcherException;
        try
        {
            if (_smokeTest)
            {
                _smokeDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "CompanionDesktopPet-smoke",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_smokeDirectory);
            }

            var settingsService = new SettingsService(_smokeDirectory);
            var settings = await settingsService.LoadAsync();
            var agentMemoryService = new AgentMemoryService(_smokeDirectory);
            var agentMemory = await agentMemoryService.LoadAsync();
            var window = new MainWindow(settings, settingsService, agentMemoryService, agentMemory);
            if (_smokeTest)
            {
                window.ContentRendered += HandleSmokeContentRendered;
            }

            MainWindow = window;
            window.Show();
        }
        catch when (_smokeTest)
        {
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= HandleDispatcherException;
        _instanceGuard?.Dispose();
        if (_smokeDirectory is not null)
        {
            try
            {
                Directory.Delete(_smokeDirectory, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        base.OnExit(e);
    }

    private async void HandleSmokeContentRendered(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window)
        {
            Shutdown(1);
            return;
        }

        window.ContentRendered -= HandleSmokeContentRendered;
        try
        {
            if (!window.TryVerifySmokeReadiness(out _))
            {
                Shutdown(1);
                return;
            }

            if (!await window.RunSmokeActionProbeAsync())
            {
                Shutdown(1);
                return;
            }

            window.Close();
        }
        catch
        {
            Shutdown(1);
        }
    }

    private void HandleDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_smokeTest)
        {
            e.Handled = true;
            Shutdown(1);
            return;
        }

        System.Windows.MessageBox.Show(
            "桌宠遇到问题，需要先休息一下。\n\n" + e.Exception.Message,
            "角色桌宠",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
