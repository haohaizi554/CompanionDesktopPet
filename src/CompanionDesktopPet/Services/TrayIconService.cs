using System.Diagnostics;
using System.Drawing;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CompanionDesktopPet.Services;

public readonly record struct TrayMenuState(
    bool IsWindowVisible,
    bool IsPaused,
    bool IsAutoStartEnabled,
    bool IsAutoStartAvailable);

internal interface ITrayShellIcon : IDisposable
{
    Icon? Icon { get; set; }
    string Text { get; set; }
    Forms.ContextMenuStrip? ContextMenuStrip { get; set; }
    bool Visible { set; }
    event EventHandler? DoubleClick;
}

internal sealed class WinFormsTrayShellIcon : ITrayShellIcon
{
    private readonly Forms.NotifyIcon _notifyIcon = new();

    public Icon? Icon
    {
        get => _notifyIcon.Icon;
        set => _notifyIcon.Icon = value;
    }

    public string Text
    {
        get => _notifyIcon.Text;
        set => _notifyIcon.Text = value;
    }

    public Forms.ContextMenuStrip? ContextMenuStrip
    {
        get => _notifyIcon.ContextMenuStrip;
        set => _notifyIcon.ContextMenuStrip = value;
    }

    public bool Visible
    {
        set => _notifyIcon.Visible = value;
    }

    public event EventHandler? DoubleClick
    {
        add => _notifyIcon.DoubleClick += value;
        remove => _notifyIcon.DoubleClick -= value;
    }

    public void Dispose() => _notifyIcon.Dispose();
}

public sealed class TrayIconService : IDisposable
{
    internal const string AutoStartUnavailableToolTip =
        "Windows 暂时不允许读取开机启动设置。";

    private readonly object _lifetimeGate = new();
    private readonly Dispatcher _dispatcher;
    private readonly Func<TrayMenuState> _getState;
    private readonly Action<Exception> _reportCommandException;
    private readonly ITrayShellIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly Icon _ownedIcon;
    private readonly List<Action> _unsubscribeActions = [];
    private bool _disposed;
    private bool _nativeResourcesDisposed;
    private bool _exitRequested;
    private bool _iconPublished;

    internal Forms.ToolStripMenuItem ShowHideMenuItem { get; }
    internal Forms.ToolStripMenuItem SayMenuItem { get; }
    internal Forms.ToolStripMenuItem PauseMenuItem { get; }
    internal Forms.ToolStripMenuItem AutoStartMenuItem { get; }
    internal Forms.ToolStripMenuItem ExitMenuItem { get; }
    internal Icon OwnedIcon => _ownedIcon;

    public TrayIconService(
        Dispatcher dispatcher,
        Icon icon,
        Func<TrayMenuState> getState,
        Action toggleVisibility,
        Action say,
        Func<Task> togglePause,
        Action toggleAutoStart,
        Func<Task> exit)
        : this(
            dispatcher,
            icon,
            getState,
            toggleVisibility,
            say,
            togglePause,
            toggleAutoStart,
            exit,
            publishIcon: true,
            () => new WinFormsTrayShellIcon(),
            ReportCommandException)
    {
    }

    internal TrayIconService(
        Dispatcher dispatcher,
        Icon icon,
        Func<TrayMenuState> getState,
        Action toggleVisibility,
        Action say,
        Func<Task> togglePause,
        Action toggleAutoStart,
        Func<Task> exit,
        bool publishIcon)
        : this(
            dispatcher,
            icon,
            getState,
            toggleVisibility,
            say,
            togglePause,
            toggleAutoStart,
            exit,
            publishIcon,
            () => new WinFormsTrayShellIcon(),
            ReportCommandException)
    {
    }

    internal TrayIconService(
        Dispatcher dispatcher,
        Icon icon,
        Func<TrayMenuState> getState,
        Action toggleVisibility,
        Action say,
        Func<Task> togglePause,
        Action toggleAutoStart,
        Func<Task> exit,
        bool publishIcon,
        Func<ITrayShellIcon> createNotifyIcon,
        Action<Exception> reportCommandException)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "TrayIconService must be constructed on its dispatcher thread.");
        }

        ArgumentNullException.ThrowIfNull(icon);
        _getState = getState ?? throw new ArgumentNullException(nameof(getState));
        ArgumentNullException.ThrowIfNull(toggleVisibility);
        ArgumentNullException.ThrowIfNull(say);
        ArgumentNullException.ThrowIfNull(togglePause);
        ArgumentNullException.ThrowIfNull(toggleAutoStart);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(createNotifyIcon);
        _reportCommandException = reportCommandException
            ?? throw new ArgumentNullException(nameof(reportCommandException));

        Icon? ownedIcon = null;
        Forms.ContextMenuStrip? contextMenu = null;
        ITrayShellIcon? notifyIcon = null;
        var publishAttempted = false;
        try
        {
            ownedIcon = (Icon)icon.Clone();
            ShowHideMenuItem = new Forms.ToolStripMenuItem();
            SayMenuItem = new Forms.ToolStripMenuItem("说句话 ♡");
            PauseMenuItem = new Forms.ToolStripMenuItem();
            AutoStartMenuItem = new Forms.ToolStripMenuItem("开机自启动")
            {
                CheckOnClick = false
            };
            ExitMenuItem = new Forms.ToolStripMenuItem("先休息啦（退出）");

            contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.AddRange(
            [
                ShowHideMenuItem,
                SayMenuItem,
                PauseMenuItem,
                AutoStartMenuItem,
                new Forms.ToolStripSeparator(),
                ExitMenuItem
            ]);

            EventHandler showHideClick = (_, _) => Dispatch(toggleVisibility);
            ShowHideMenuItem.Click += showHideClick;
            _unsubscribeActions.Add(() => ShowHideMenuItem.Click -= showHideClick);
            EventHandler sayClick = (_, _) => Dispatch(say);
            SayMenuItem.Click += sayClick;
            _unsubscribeActions.Add(() => SayMenuItem.Click -= sayClick);
            EventHandler pauseClick = (_, _) => Dispatch(togglePause);
            PauseMenuItem.Click += pauseClick;
            _unsubscribeActions.Add(() => PauseMenuItem.Click -= pauseClick);
            EventHandler autoStartClick = (_, _) => Dispatch(toggleAutoStart);
            AutoStartMenuItem.Click += autoStartClick;
            _unsubscribeActions.Add(() => AutoStartMenuItem.Click -= autoStartClick);
            EventHandler exitClick = (_, _) => DispatchExit(exit);
            ExitMenuItem.Click += exitClick;
            _unsubscribeActions.Add(() => ExitMenuItem.Click -= exitClick);
            System.ComponentModel.CancelEventHandler menuOpening = (_, _) => RefreshMenu();
            contextMenu.Opening += menuOpening;
            _unsubscribeActions.Add(() => contextMenu.Opening -= menuOpening);

            notifyIcon = createNotifyIcon()
                ?? throw new InvalidOperationException("The tray icon factory returned null.");
            notifyIcon.Icon = ownedIcon;
            notifyIcon.Text = "佳怡桌宠";
            notifyIcon.ContextMenuStrip = contextMenu;
            EventHandler doubleClick = (_, _) => Dispatch(toggleVisibility);
            notifyIcon.DoubleClick += doubleClick;
            _unsubscribeActions.Add(() => notifyIcon.DoubleClick -= doubleClick);

            _ownedIcon = ownedIcon;
            _contextMenu = contextMenu;
            _notifyIcon = notifyIcon;
            RefreshMenu();
            if (publishIcon)
            {
                publishAttempted = true;
                notifyIcon.Visible = true;
                _iconPublished = true;
            }
        }
        catch
        {
            DetachEventHandlers();
            CleanupNativeResources(
                notifyIcon,
                contextMenu,
                ownedIcon,
                hideNotifyIcon: publishAttempted);
            throw;
        }
    }

    internal void RefreshMenu()
    {
        if (IsDisposedOrExiting())
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ApplyMenuState();
            return;
        }

        _dispatcher.Invoke(ApplyMenuState);
    }

    private void ApplyMenuState()
    {
        if (IsDisposedOrExiting())
        {
            return;
        }

        var state = _getState();
        ShowHideMenuItem.Text = state.IsWindowVisible ? "藏起佳怡" : "显示佳怡";
        PauseMenuItem.Text = state.IsPaused ? "继续动画" : "暂停动画";
        AutoStartMenuItem.Checked = state.IsAutoStartEnabled;
        AutoStartMenuItem.Enabled = state.IsAutoStartAvailable;
        AutoStartMenuItem.ToolTipText = state.IsAutoStartAvailable
            ? string.Empty
            : AutoStartUnavailableToolTip;
    }

    private void Dispatch(Action action) => Dispatch(
        () =>
        {
            action();
            return Task.CompletedTask;
        },
        isExit: false);

    private void Dispatch(Func<Task> action) => Dispatch(action, isExit: false);

    private void DispatchExit(Func<Task> action) => Dispatch(action, isExit: true);

    private void Dispatch(Func<Task> action, bool isExit)
    {
        lock (_lifetimeGate)
        {
            if (_disposed || _exitRequested)
            {
                return;
            }

            if (isExit)
            {
                _exitRequested = true;
            }
        }

        Task commandTask;
        try
        {
            commandTask = _dispatcher.InvokeAsync(async () =>
            {
                lock (_lifetimeGate)
                {
                    if (_disposed || (_exitRequested && !isExit))
                    {
                        return;
                    }
                }

                await action();
            }).Task.Unwrap();
        }
        catch (Exception exception)
        {
            _reportCommandException(exception);
            return;
        }

        _ = ObserveCommandAsync(commandTask);
    }

    private async Task ObserveCommandAsync(Task commandTask)
    {
        try
        {
            await commandTask;
        }
        catch (Exception exception)
        {
            _reportCommandException(exception);
        }
    }

    private bool IsDisposedOrExiting()
    {
        lock (_lifetimeGate)
        {
            return _disposed || _exitRequested;
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_dispatcher.CheckAccess())
        {
            DisposeNativeResources();
            return;
        }

        if (!_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished)
        {
            try
            {
                _dispatcher.Invoke(DisposeNativeResources);
                return;
            }
            catch (InvalidOperationException) when (
                _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
            }
        }

        DisposeNativeResources();
    }

    private void DisposeNativeResources()
    {
        lock (_lifetimeGate)
        {
            if (_nativeResourcesDisposed)
            {
                return;
            }

            _nativeResourcesDisposed = true;
        }

        DetachEventHandlers();
        CleanupNativeResources(
            _notifyIcon,
            _contextMenu,
            _ownedIcon,
            hideNotifyIcon: _iconPublished);
    }

    private void DetachEventHandlers()
    {
        foreach (var unsubscribe in _unsubscribeActions)
        {
            TryCleanup(unsubscribe);
        }

        _unsubscribeActions.Clear();
    }

    private void CleanupNativeResources(
        ITrayShellIcon? notifyIcon,
        Forms.ContextMenuStrip? contextMenu,
        Icon? ownedIcon,
        bool hideNotifyIcon)
    {
        if (hideNotifyIcon)
        {
            TryCleanup(() =>
            {
                if (notifyIcon is not null)
                {
                    notifyIcon.Visible = false;
                }
            });
        }

        TryCleanup(() => notifyIcon?.Dispose());
        TryCleanup(() => contextMenu?.Dispose());
        TryCleanup(() => ownedIcon?.Dispose());
    }

    private void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            try
            {
                _reportCommandException(new InvalidOperationException(
                    "Tray cleanup failed.",
                    exception));
            }
            catch (Exception reportingFailure) when (!IsFatalException(reportingFailure))
            {
                // Cleanup remains best-effort even if diagnostics are unavailable.
            }
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    private static void ReportCommandException(Exception exception) =>
        Trace.TraceError("Tray command failed: {0}", exception);
}
