using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CompanionDesktopPet.Services;
using CompanionDesktopPet.UI;

namespace CompanionDesktopPet;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan SmokeWarmupTimeout = TimeSpan.FromSeconds(15);
    private SingleInstanceGuard? _instanceGuard;
    private IAutoStartService? _autoStartService;
    private TrayIconService? _trayIconService;
    private PetThemeManager? _themeManager;
    private bool _smokeTest;
    private string? _smokeDirectory;
    private int _exitStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _smokeTest = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        var instanceName = _smokeTest
            ? $"Local\\CompanionDesktopPet-Smoke-{Environment.ProcessId}-{Guid.NewGuid():N}"
            : "Local\\CompanionDesktopPet-7E5D78F4";
        _instanceGuard = new SingleInstanceGuard(instanceName);
        if (!ShouldContinuePrimaryStartup(_instanceGuard))
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += HandleDispatcherException;
        try
        {
            _themeManager = new PetThemeManager(
                Resources,
                Dispatcher,
                new SystemHighContrastProvider());
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
            var agentMemory = await agentMemoryService.LoadForDeferredWarmupAsync();
            var factories = AppSystemIntegrationFactories.Default;
            _autoStartService = CreateAutoStartService(_smokeTest, factories);
            var window = new MainWindow(
                settings,
                settingsService,
                agentMemoryService,
                agentMemory,
                _autoStartService);
            if (_smokeTest)
            {
                window.ContentRendered += HandleSmokeContentRendered;
            }

            MainWindow = window;
            window.Show();
            RegisterPrimaryActivation(
                _instanceGuard,
                Dispatcher,
                () => Volatile.Read(ref _exitStarted) != 0
                    || !ReferenceEquals(MainWindow, window),
                window.RestoreFromSecondInstance);
            _trayIconService = TryCreateTrayService(_smokeTest, window, factories);
        }
        catch when (_smokeTest)
        {
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Interlocked.Exchange(ref _exitStarted, 1);
        DispatcherUnhandledException -= HandleDispatcherException;
        _themeManager?.Dispose();
        _themeManager = null;
        var trayIconService = _trayIconService;
        _trayIconService = null;
        var instanceGuard = _instanceGuard;
        _instanceGuard = null;
        try
        {
            DisposeOwnedIntegrations(
                trayIconService,
                instanceGuard,
                DeleteSmokeDirectory);
        }
        finally
        {
            base.OnExit(e);
        }
    }

    internal static bool ShouldContinuePrimaryStartup(SingleInstanceGuard instanceGuard)
    {
        ArgumentNullException.ThrowIfNull(instanceGuard);
        if (instanceGuard.IsPrimaryInstance)
        {
            return true;
        }

        instanceGuard.SignalPrimaryInstance();
        return false;
    }

    internal static void RegisterPrimaryActivation(
        SingleInstanceGuard instanceGuard,
        Dispatcher dispatcher,
        Func<bool> isExiting,
        Action restoreWindow)
    {
        ArgumentNullException.ThrowIfNull(instanceGuard);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isExiting);
        ArgumentNullException.ThrowIfNull(restoreWindow);
        instanceGuard.RegisterActivationCallback(
            () => QueuePrimaryActivation(dispatcher, isExiting, restoreWindow));
    }

    private static void QueuePrimaryActivation(
        Dispatcher dispatcher,
        Func<bool> isExiting,
        Action restoreWindow)
    {
        if (isExiting()
            || dispatcher.HasShutdownStarted
            || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!isExiting()
                        && !dispatcher.HasShutdownStarted
                        && !dispatcher.HasShutdownFinished)
                    {
                        restoreWindow();
                    }
                }
                catch (Exception exception) when (!IsFatalIntegrationException(exception))
                {
                    System.Diagnostics.Trace.TraceError(
                        "Could not restore the primary window: {0}",
                        exception);
                }
            });
        }
        catch (Exception exception) when (!IsFatalIntegrationException(exception))
        {
            System.Diagnostics.Trace.TraceError(
                "Could not queue primary activation: {0}",
                exception);
        }
    }

    internal static IAutoStartService CreateAutoStartService(
        bool smokeTest,
        AppSystemIntegrationFactories factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        return smokeTest
            ? DisabledAutoStartService.Instance
            : factories.CreateWindowsAutoStartService();
    }

    internal static TrayIconService? TryCreateTrayService(
        bool smokeTest,
        MainWindow? window,
        AppSystemIntegrationFactories factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        if (smokeTest)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(window);
        TrayIconService? trayIconService = null;
        try
        {
            using var stream = factories.LoadTrayIconStream()
                ?? throw new FileNotFoundException("The tray icon resource is unavailable.");
            using var sourceIcon = new Icon(stream);
            trayIconService = factories.CreateTrayIconService(
                window.Dispatcher,
                sourceIcon,
                window);
            window.SetTrayAvailability(true);
            return trayIconService;
        }
        catch (Exception exception) when (!IsFatalIntegrationException(exception))
        {
            trayIconService?.Dispose();
            window.SetTrayAvailability(false);
            return null;
        }
    }

    private static bool IsFatalIntegrationException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    internal static Stream? LoadTrayIconStream()
    {
        var resource = GetResourceStream(new Uri(
            "/CompanionDesktopPet;component/Assets/pet.ico",
            UriKind.Relative));
        return resource?.Stream;
    }

    internal static void DisposeOwnedIntegrations(
        IDisposable? trayIconService,
        IDisposable? instanceGuard,
        Action finalCleanup)
    {
        ArgumentNullException.ThrowIfNull(finalCleanup);
        try
        {
            trayIconService?.Dispose();
        }
        finally
        {
            try
            {
                instanceGuard?.Dispose();
            }
            finally
            {
                finalCleanup();
            }
        }
    }

    private void DeleteSmokeDirectory()
    {
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
            if (!await window.PrepareSmokeReadinessAsync(SmokeWarmupTimeout)
                || !window.TryVerifySmokeReadiness(out _))
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

internal sealed class AppSystemIntegrationFactories
{
    internal static AppSystemIntegrationFactories Default { get; } = new(
        () => new WindowsAutoStartService(),
        App.LoadTrayIconStream,
        static (dispatcher, icon, window) => new TrayIconService(
            dispatcher,
            icon,
            window.GetTrayMenuState,
            window.ToggleVisibilityFromTray,
            window.SaySomething,
            window.ToggleAnimationAsync,
            window.ToggleAutoStartFromTray,
            window.RequestExitAsync));

    internal AppSystemIntegrationFactories(
        Func<IAutoStartService> createWindowsAutoStartService,
        Func<Stream?> loadTrayIconStream,
        Func<Dispatcher, Icon, MainWindow, TrayIconService> createTrayIconService)
    {
        CreateWindowsAutoStartService = createWindowsAutoStartService
            ?? throw new ArgumentNullException(nameof(createWindowsAutoStartService));
        LoadTrayIconStream = loadTrayIconStream
            ?? throw new ArgumentNullException(nameof(loadTrayIconStream));
        CreateTrayIconService = createTrayIconService
            ?? throw new ArgumentNullException(nameof(createTrayIconService));
    }

    internal Func<IAutoStartService> CreateWindowsAutoStartService { get; }
    internal Func<Stream?> LoadTrayIconStream { get; }
    internal Func<Dispatcher, Icon, MainWindow, TrayIconService> CreateTrayIconService { get; }
}
