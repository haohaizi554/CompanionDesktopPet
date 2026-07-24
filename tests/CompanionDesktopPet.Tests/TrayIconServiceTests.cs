using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using CompanionDesktopPet.Services;
using Forms = System.Windows.Forms;

namespace CompanionDesktopPet.Tests;

public sealed class TrayIconServiceTests
{
    private static readonly Lazy<StaTestHost> StaHost = new(() => new StaTestHost());

    [Fact]
    public void RefreshMenu_UsesCurrentStateIncludingUnavailableAutoStart()
    {
        RunOnStaThread(() =>
        {
            using var source = LoadTestIcon();
            var shell = new FakeTrayShellIcon();
            var state = new TrayMenuState(
                IsWindowVisible: false,
                IsPaused: true,
                IsAutoStartEnabled: true,
                IsAutoStartAvailable: false);
            var service = CreateService(source, shell, () => state);

            service.RefreshMenu();

            Assert.Equal("显示佳怡", service.ShowHideMenuItem.Text);
            Assert.Equal("继续动画", service.PauseMenuItem.Text);
            Assert.True(service.AutoStartMenuItem.Checked);
            Assert.False(service.AutoStartMenuItem.Enabled);
            Assert.Equal(
                "Windows 暂时不允许读取开机启动设置。",
                service.AutoStartMenuItem.ToolTipText);

            state = new TrayMenuState(
                IsWindowVisible: true,
                IsPaused: false,
                IsAutoStartEnabled: false,
                IsAutoStartAvailable: true);
            service.RefreshMenu();

            Assert.Equal("藏起佳怡", service.ShowHideMenuItem.Text);
            Assert.Equal("暂停动画", service.PauseMenuItem.Text);
            Assert.False(service.AutoStartMenuItem.Checked);
            Assert.True(service.AutoStartMenuItem.Enabled);
            Assert.Equal(string.Empty, service.AutoStartMenuItem.ToolTipText);
            service.Dispose();
            Assert.Equal(0, shell.VisibleTrueCount);
            Assert.Equal(0, shell.VisibleFalseCount);
        });
    }

    [Fact]
    public async Task CommandsAndDoubleClick_AreDispatcherRoutedAndAsyncFailuresAreObserved()
    {
        await RunOnStaThreadAsync(async () =>
        {
            using var source = LoadTestIcon();
            var shell = new FakeTrayShellIcon();
            var calls = new List<string>();
            var observed = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var service = new TrayIconService(
                Dispatcher.CurrentDispatcher,
                source,
                () => new TrayMenuState(true, false, false, true),
                () => calls.Add("toggle"),
                () => calls.Add("say"),
                () =>
                {
                    calls.Add("pause");
                    return Task.FromException(new InvalidOperationException("pause failed"));
                },
                () => calls.Add("auto-start"),
                () =>
                {
                    calls.Add("exit");
                    return Task.CompletedTask;
                },
                publishIcon: false,
                () => shell,
                exception => observed.TrySetResult(exception));

            service.ShowHideMenuItem.PerformClick();
            service.SayMenuItem.PerformClick();
            service.PauseMenuItem.PerformClick();
            service.AutoStartMenuItem.PerformClick();
            shell.RaiseDoubleClick();
            await DrainDispatcherAsync();

            Assert.Equal(
                ["toggle", "say", "pause", "auto-start", "toggle"],
                calls);
            var failure = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("pause failed", failure.Message);
            Assert.Equal(0, shell.VisibleTrueCount);
        });
    }

    [Fact]
    public async Task DefaultAsyncFailureReporter_ObservesWithoutCrashingTheDispatcher()
    {
        await RunOnStaThreadAsync(async () =>
        {
            using var source = LoadTestIcon();
            var listener = new RecordingTraceListener();
            Trace.Listeners.Add(listener);
            try
            {
                using var service = new TrayIconService(
                    Dispatcher.CurrentDispatcher,
                    source,
                    () => new TrayMenuState(true, false, false, true),
                    () => { },
                    () => { },
                    () => Task.FromException(new InvalidOperationException("observed failure")),
                    () => { },
                    () => Task.CompletedTask,
                    publishIcon: false);

                service.PauseMenuItem.PerformClick();
                await DrainDispatcherAsync();

                Assert.Contains("observed failure", listener.Output, StringComparison.Ordinal);
                Assert.False(Dispatcher.CurrentDispatcher.HasShutdownStarted);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                listener.Dispose();
            }
        });
    }

    [Fact]
    public async Task Dispose_SuppressesQueuedCommandsAndIsIdempotentBestEffort()
    {
        await RunOnStaThreadAsync(async () =>
        {
            using var source = LoadTestIcon();
            var shell = new FakeTrayShellIcon { ThrowWhenHidden = true };
            var calls = 0;
            var service = CreateService(
                source,
                shell,
                () => new TrayMenuState(true, false, false, true),
                toggleVisibility: () => calls++,
                publishIcon: true);

            service.ShowHideMenuItem.PerformClick();
            service.Dispose();
            service.Dispose();
            await DrainDispatcherAsync();

            Assert.Equal(0, calls);
            Assert.Equal(1, shell.VisibleFalseCount);
            Assert.Equal(1, shell.DisposeCount);
            Assert.True(shell.ContextMenuWasAliveWhenDisposed);
            Assert.True(shell.IconWasAliveWhenDisposed);
        });
    }

    [Fact]
    public async Task ExitStartsOnceAndSuppressesEveryLaterCommand()
    {
        await RunOnStaThreadAsync(async () =>
        {
            using var source = LoadTestIcon();
            var shell = new FakeTrayShellIcon();
            var toggles = 0;
            var exits = 0;
            using var service = new TrayIconService(
                Dispatcher.CurrentDispatcher,
                source,
                () => new TrayMenuState(true, false, false, true),
                () => toggles++,
                () => toggles++,
                () =>
                {
                    toggles++;
                    return Task.CompletedTask;
                },
                () => toggles++,
                () =>
                {
                    exits++;
                    return Task.CompletedTask;
                },
                publishIcon: false,
                () => shell,
                _ => { });

            service.ExitMenuItem.PerformClick();
            service.ExitMenuItem.PerformClick();
            service.ShowHideMenuItem.PerformClick();
            service.SayMenuItem.PerformClick();
            await DrainDispatcherAsync();

            Assert.Equal(1, exits);
            Assert.Equal(0, toggles);
        });
    }

    [Fact]
    public void PublishFailure_CleansEveryCreatedNativeResourceBeforeRethrowing()
    {
        RunOnStaThread(() =>
        {
            using var source = LoadTestIcon();
            var shell = new FakeTrayShellIcon { ThrowWhenPublished = true };

            Assert.Throws<InvalidOperationException>(() => new TrayIconService(
                Dispatcher.CurrentDispatcher,
                source,
                () => new TrayMenuState(true, false, false, true),
                () => { },
                () => { },
                () => Task.CompletedTask,
                () => { },
                () => Task.CompletedTask,
                publishIcon: true,
                () => shell,
                _ => { }));

            Assert.Equal(1, shell.VisibleTrueCount);
            Assert.Equal(1, shell.VisibleFalseCount);
            Assert.Equal(1, shell.DisposeCount);
            Assert.True(shell.ContextMenuWasAliveWhenDisposed);
            Assert.True(shell.IconWasAliveWhenDisposed);
        });
    }

    [Fact]
    public void ServiceClone_RemainsUsableAfterSourceIconAndStreamAreDisposed()
    {
        RunOnStaThread(() =>
        {
            var stream = File.OpenRead(TestIconPath());
            var source = new Icon(stream);
            var shell = new FakeTrayShellIcon();
            using var service = CreateService(
                source,
                shell,
                () => new TrayMenuState(true, false, false, true));

            source.Dispose();
            stream.Dispose();

            var field = typeof(TrayIconService).GetField(
                "_ownedIcon",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var ownedIcon = Assert.IsType<Icon>(field!.GetValue(service));
            Assert.NotEqual(IntPtr.Zero, ownedIcon.Handle);
            Assert.Same(ownedIcon, shell.Icon);
        });
    }

    private static TrayIconService CreateService(
        Icon source,
        FakeTrayShellIcon shell,
        Func<TrayMenuState> getState,
        Action? toggleVisibility = null,
        bool publishIcon = false) =>
        new(
            Dispatcher.CurrentDispatcher,
            source,
            getState,
            toggleVisibility ?? (() => { }),
            () => { },
            () => Task.CompletedTask,
            () => { },
            () => Task.CompletedTask,
            publishIcon,
            () => shell,
            _ => { });

    private static Icon LoadTestIcon() => new(TestIconPath());

    private static string TestIconPath() =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "pet.ico");

    private static void RunOnStaThread(Action action) => StaHost.Value.Invoke(action);

    private static Task RunOnStaThreadAsync(Func<Task> action) =>
        StaHost.Value.InvokeAsync(action);

    private static async Task DrainDispatcherAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(20);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private sealed class FakeTrayShellIcon : ITrayShellIcon
    {
        private bool _visible;

        public bool ThrowWhenPublished { get; init; }
        public bool ThrowWhenHidden { get; init; }
        public int VisibleTrueCount { get; private set; }
        public int VisibleFalseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool ContextMenuWasAliveWhenDisposed { get; private set; }
        public bool IconWasAliveWhenDisposed { get; private set; }
        public Icon? Icon { get; set; }
        public string Text { get; set; } = string.Empty;
        public Forms.ContextMenuStrip? ContextMenuStrip { get; set; }

        public bool Visible
        {
            set
            {
                _visible = value;
                if (value)
                {
                    VisibleTrueCount++;
                    if (ThrowWhenPublished)
                    {
                        throw new InvalidOperationException("publish failed");
                    }

                    return;
                }

                VisibleFalseCount++;
                if (ThrowWhenHidden)
                {
                    throw new InvalidOperationException("hide failed");
                }
            }
        }

        public event EventHandler? DoubleClick;

        public void Dispose()
        {
            DisposeCount++;
            ContextMenuWasAliveWhenDisposed = ContextMenuStrip is { IsDisposed: false };
            try
            {
                IconWasAliveWhenDisposed = Icon is not null && Icon.Handle != IntPtr.Zero;
            }
            catch (ObjectDisposedException)
            {
                IconWasAliveWhenDisposed = false;
            }
        }

        public void RaiseDoubleClick() => DoubleClick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RecordingTraceListener : TraceListener
    {
        private readonly StringWriter _output = new();

        public string Output => _output.ToString();

        public override void Write(string? message) => _output.Write(message);

        public override void WriteLine(string? message) => _output.WriteLine(message);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _output.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class StaTestHost
    {
        private readonly Dispatcher _dispatcher;

        public StaTestHost()
        {
            Dispatcher? dispatcher = null;
            Exception? initializationException = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
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

                if (initializationException is null)
                {
                    Dispatcher.Run();
                }
            })
            {
                IsBackground = true,
                Name = "CompanionDesktopPet.TrayTests"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (initializationException is not null)
            {
                ExceptionDispatchInfo.Capture(initializationException).Throw();
            }

            _dispatcher = dispatcher
                ?? throw new InvalidOperationException("The tray test dispatcher did not start.");
        }

        public void Invoke(Action action) => _dispatcher.Invoke(action);

        public Task InvokeAsync(Func<Task> action) =>
            _dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}
