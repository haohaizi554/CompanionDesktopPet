using System.Drawing;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using CompanionDesktopPet.Models;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class AppLifecycleTests
{
    private static readonly Lazy<StaTestHost> StaHost = new(() => new StaTestHost());

    [Fact]
    public void SmokeComposition_CallsNoWindowsOrTrayFactories()
    {
        var autoStartCalls = 0;
        var iconLoaderCalls = 0;
        var trayFactoryCalls = 0;
        var factories = new AppSystemIntegrationFactories(
            () =>
            {
                autoStartCalls++;
                return new WindowsAutoStartService();
            },
            () =>
            {
                iconLoaderCalls++;
                return null;
            },
            (_, _, _) =>
            {
                trayFactoryCalls++;
                throw new InvalidOperationException();
            });

        var autoStart = App.CreateAutoStartService(smokeTest: true, factories);
        var tray = App.TryCreateTrayService(
            smokeTest: true,
            window: null,
            factories);

        Assert.Same(DisabledAutoStartService.Instance, autoStart);
        Assert.Null(tray);
        Assert.Equal(0, autoStartCalls);
        Assert.Equal(0, iconLoaderCalls);
        Assert.Equal(0, trayFactoryCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrayInitializationFailure_PreservesAVisibleRecoverableWindow(bool missingIcon)
    {
        RunOnStaThread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(directory),
                null,
                null,
                idleTimeProvider: null,
                suppressApplicationShutdownOnClose: true,
                shutdownApplication: null,
                new AmbientActionScheduler(() => 0.5),
                DisabledAutoStartService.Instance);
            var trayFactoryCalls = 0;
            try
            {
                window.Show();
                window.WindowState = WindowState.Minimized;
                var factories = new AppSystemIntegrationFactories(
                    () => DisabledAutoStartService.Instance,
                    () => missingIcon ? null : File.OpenRead(TestIconPath()),
                    (_, _, _) =>
                    {
                        trayFactoryCalls++;
                        throw new InvalidOperationException("tray publish failed");
                    });

                var tray = App.TryCreateTrayService(
                    smokeTest: false,
                    window,
                    factories);

                Assert.Null(tray);
                Assert.Equal(missingIcon ? 0 : 1, trayFactoryCalls);
                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(window.IsActive);
                var hideItem = Assert.IsType<System.Windows.Controls.MenuItem>(
                    window.FindName("HideToTrayMenuItem"));
                Assert.False(hideItem.IsEnabled);
                window.HideToTray();
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        });
    }

    [Fact]
    public void InvalidTrayIcon_PreservesAVisibleWindowAndSkipsTheTrayFactory()
    {
        RunOnStaThread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var window = new MainWindow(
                PetSettings.Default,
                new SettingsService(directory),
                null,
                null,
                idleTimeProvider: null,
                suppressApplicationShutdownOnClose: true,
                shutdownApplication: null,
                new AmbientActionScheduler(() => 0.5),
                DisabledAutoStartService.Instance);
            var trayFactoryCalls = 0;
            try
            {
                window.Show();
                var factories = new AppSystemIntegrationFactories(
                    () => DisabledAutoStartService.Instance,
                    () => new MemoryStream([0x01, 0x02, 0x03]),
                    (_, _, _) =>
                    {
                        trayFactoryCalls++;
                        throw new InvalidOperationException();
                    });

                var tray = App.TryCreateTrayService(false, window, factories);

                Assert.Null(tray);
                Assert.Equal(0, trayFactoryCalls);
                Assert.True(window.IsVisible);
                window.HideToTray();
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        });
    }

    [Fact]
    public void Cleanup_AlwaysReleasesGuardAndSmokeDirectoryAfterTrayFailure()
    {
        var tray = new CountingDisposable(throwOnDispose: true);
        var guard = new CountingDisposable(throwOnDispose: false);
        var smokeCleanupCalls = 0;

        Assert.Throws<InvalidOperationException>(() => App.DisposeOwnedIntegrations(
            tray,
            guard,
            () => smokeCleanupCalls++));

        Assert.Equal(1, tray.DisposeCount);
        Assert.Equal(1, guard.DisposeCount);
        Assert.Equal(1, smokeCleanupCalls);
    }

    [Fact]
    public void PackResourceIconLoader_ReturnsAReadableIcon()
    {
        RunOnStaThread(() =>
        {
            using var stream = App.LoadTrayIconStream();
            Assert.NotNull(stream);
            using var icon = new Icon(stream!);
            Assert.NotEqual(IntPtr.Zero, icon.Handle);
        });
    }

    private static string TestIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "pet.ico");

    private static void RunOnStaThread(Action action) => StaHost.Value.Invoke(action);

    private sealed class CountingDisposable(bool throwOnDispose) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (throwOnDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }
    }

    private sealed class StaTestHost
    {
        private readonly Application _application;
        private readonly Dispatcher _dispatcher;

        public StaTestHost()
        {
            if (Application.Current is { } existingApplication)
            {
                _application = existingApplication;
                _dispatcher = existingApplication.Dispatcher;
                return;
            }

            Application? application = null;
            Dispatcher? dispatcher = null;
            Exception? initializationException = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    application = new Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(
                            "/CompanionDesktopPet;component/Themes/PetTheme.xaml",
                            UriKind.Relative)
                    });
                    dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception exception)
                {
                    initializationException = exception;
                }
                finally
                {
                    ready.Set();
                }

                if (initializationException is null && !dispatcher!.HasShutdownStarted)
                {
                    if (ReferenceEquals(application!.Dispatcher, dispatcher))
                    {
                        application.Run();
                    }
                    else
                    {
                        Dispatcher.Run();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "CompanionDesktopPet.AppLifecycleTests"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (initializationException is not null)
            {
                ExceptionDispatchInfo.Capture(initializationException).Throw();
            }

            _application = application
                ?? throw new InvalidOperationException("The WPF test application did not start.");
            _dispatcher = dispatcher
                ?? throw new InvalidOperationException("The WPF test dispatcher did not start.");
        }

        public void Invoke(Action action) => _dispatcher.Invoke(action);
    }
}
