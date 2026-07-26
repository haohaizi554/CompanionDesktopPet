using CompanionDesktopPet.Services;
using System.Runtime.InteropServices;

namespace CompanionDesktopPet.Tests;

public sealed class WindowsForegroundFullscreenDetectorTests
{
    private static readonly NativePixelRect FullHd = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(0, 99)]
    [InlineData(99, 99)]
    public void Observe_ZeroOrExcludedForeground_ReturnsUnknownAfterOneRead(
        long foreground,
        long excluded)
    {
        var native = new QueueForegroundFullscreenNative();
        native.ForegroundWindows.Enqueue((nint)foreground);

        var result = new WindowsForegroundFullscreenDetector(native).Observe((nint)excluded);

        Assert.Null(result);
        Assert.Equal(1, native.ForegroundReadCount);
    }

    [Fact]
    public void Observe_InvalidWindowTwice_ReturnsUnknownAfterTwoStableAttempts()
    {
        var native = StableFullscreenNative();
        native.IsWindowResult = false;
        QueueStableAttempts(native, 17, 2);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.Null(result);
        Assert.Equal(4, native.ForegroundReadCount);
    }

    [Fact]
    public void Observe_WindowStyleFailureTwice_ReturnsUnknownAfterTwoStableAttempts()
    {
        var native = StableFullscreenNative();
        native.TryGetWindowStyleResult = false;
        QueueStableAttempts(native, 17, 2);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.Null(result);
        Assert.Equal(4, native.ForegroundReadCount);
    }

    [Fact]
    public void Observe_FirstWindowChangesThenSecondIsStable_ReturnsSecondClassification()
    {
        var native = StableFullscreenNative();
        native.DesktopWindow = 11;
        QueueForeground(native, 11, 22, 22, 22);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.True(result);
        Assert.Equal(4, native.ForegroundReadCount);
    }

    [Fact]
    public void Observe_TwoUnstableAttempts_ReturnsUnknownAfterFourReads()
    {
        var native = StableFullscreenNative();
        QueueForeground(native, 11, 12, 13, 14);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.Null(result);
        Assert.Equal(4, native.ForegroundReadCount);
    }

    [Theory]
    [InlineData(ExcludedWindowKind.Desktop)]
    [InlineData(ExcludedWindowKind.Shell)]
    [InlineData(ExcludedWindowKind.Invisible)]
    [InlineData(ExcludedWindowKind.Minimized)]
    [InlineData(ExcludedWindowKind.Child)]
    [InlineData(ExcludedWindowKind.Cloaked)]
    public void Observe_ExplicitlyNonFullscreenWindow_ReturnsFalse(ExcludedWindowKind kind)
    {
        const long foreground = 31;
        var native = StableFullscreenNative();
        QueueStableAttempts(native, foreground, 1);

        switch (kind)
        {
            case ExcludedWindowKind.Desktop:
                native.DesktopWindow = (nint)foreground;
                break;
            case ExcludedWindowKind.Shell:
                native.ShellWindow = (nint)foreground;
                break;
            case ExcludedWindowKind.Invisible:
                native.IsWindowVisibleResult = false;
                break;
            case ExcludedWindowKind.Minimized:
                native.IsWindowMinimizedResult = true;
                break;
            case ExcludedWindowKind.Child:
                native.WindowStyle = 0x40000000u;
                break;
            case ExcludedWindowKind.Cloaked:
                native.Cloaked = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.False(result);
        Assert.Equal(2, native.ForegroundReadCount);
    }

    [Theory]
    [InlineData(QueryFailure.Cloak)]
    [InlineData(QueryFailure.Frame)]
    [InlineData(QueryFailure.MonitorBounds)]
    public void Observe_NativeQueryFailureTwice_ReturnsUnknown(QueryFailure failure)
    {
        var native = StableFullscreenNative();
        QueueStableAttempts(native, 41, 2);

        switch (failure)
        {
            case QueryFailure.Cloak:
                native.TryGetCloakedResult = false;
                break;
            case QueryFailure.Frame:
                native.TryGetExtendedFrameBoundsResult = false;
                break;
            case QueryFailure.MonitorBounds:
                native.TryGetMonitorBoundsResult = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure));
        }

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.Null(result);
        Assert.Equal(4, native.ForegroundReadCount);
    }

    [Theory]
    [InlineData(InvalidRectangle.Frame)]
    [InlineData(InvalidRectangle.Monitor)]
    public void Observe_NonpositiveRectangleTwice_ReturnsUnknown(InvalidRectangle rectangle)
    {
        var native = StableFullscreenNative();
        QueueStableAttempts(native, 43, 2);

        if (rectangle == InvalidRectangle.Frame)
        {
            native.FrameBounds = new NativePixelRect(0, 0, 0, 1080);
        }
        else
        {
            native.MonitorBounds = new NativePixelRect(0, 0, 1920, 0);
        }

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.Null(result);
        Assert.Equal(4, native.ForegroundReadCount);
    }

    [Fact]
    public void Observe_ZeroMonitorHandle_ReturnsFalse()
    {
        var native = StableFullscreenNative();
        native.Monitor = 0;
        QueueStableAttempts(native, 47, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.False(result);
        Assert.Equal(2, native.ForegroundReadCount);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080)]
    [InlineData(-1, 1, 1919, 1081)]
    public void Observe_ExactOrOnePixelEdgeCoverage_ReturnsTrue(
        int left,
        int top,
        int right,
        int bottom)
    {
        var native = StableFullscreenNative();
        native.FrameBounds = new NativePixelRect(left, top, right, bottom);
        QueueStableAttempts(native, 51, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.True(result);
    }

    [Theory]
    [InlineData(-2, 0, 1920, 1080)]
    [InlineData(0, 2, 1920, 1080)]
    [InlineData(0, 0, 1918, 1080)]
    [InlineData(0, 0, 1920, 1082)]
    public void Observe_AnyEdgeOffByTwoPixels_ReturnsFalse(
        int left,
        int top,
        int right,
        int bottom)
    {
        var native = StableFullscreenNative();
        native.FrameBounds = new NativePixelRect(left, top, right, bottom);
        QueueStableAttempts(native, 53, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.False(result);
    }

    [Fact]
    public void Observe_MaximizedWorkAreaOnly_ReturnsFalse()
    {
        var native = StableFullscreenNative();
        native.FrameBounds = new NativePixelRect(0, 0, 1920, 1040);
        QueueStableAttempts(native, 59, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.False(result);
    }

    [Fact]
    public void Observe_FullMonitorWithAutoHiddenTaskbarSemantics_ReturnsTrue()
    {
        var native = StableFullscreenNative();
        native.FrameBounds = FullHd;
        native.MonitorBounds = FullHd;
        QueueStableAttempts(native, 61, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.True(result);
    }

    [Theory]
    [InlineData(-1920, -120, 0, 960)]
    [InlineData(1920, 0, 3000, 1920)]
    public void Observe_NegativeCoordinateOrPortraitMonitor_ReturnsTrue(
        int left,
        int top,
        int right,
        int bottom)
    {
        var native = StableFullscreenNative();
        var bounds = new NativePixelRect(left, top, right, bottom);
        native.FrameBounds = bounds;
        native.MonitorBounds = bounds;
        QueueStableAttempts(native, 67, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.True(result);
    }

    [Theory]
    [InlineData(-1920, 0, 1920, 1080)]
    [InlineData(-100, -100, 1820, 980)]
    [InlineData(200, 100, 1720, 980)]
    public void Observe_SpanningOffscreenOrNonmatchingFrame_ReturnsFalse(
        int left,
        int top,
        int right,
        int bottom)
    {
        var native = StableFullscreenNative();
        native.FrameBounds = new NativePixelRect(left, top, right, bottom);
        QueueStableAttempts(native, 71, 1);

        var result = new WindowsForegroundFullscreenDetector(native).Observe(0);

        Assert.False(result);
    }

    [Fact]
    public void Observe_LaterMonitorRectangleIsReadAgainInsteadOfCached()
    {
        var native = StableFullscreenNative();
        QueueStableAttempts(native, 73, 2);
        var detector = new WindowsForegroundFullscreenDetector(native);

        Assert.True(detector.Observe(0));

        native.MonitorBounds = new NativePixelRect(1920, 0, 3840, 1080);

        Assert.False(detector.Observe(0));
    }

    [Fact]
    public void Observe_NativeAdapterSmoke_DoesNotThrowAndReturnsLegalNullableBool()
    {
        bool? result = null;

        var exception = Record.Exception(
            () => result = new WindowsForegroundFullscreenDetector().Observe(0));

        Assert.Null(exception);
        Assert.Contains(result, new bool?[] { null, false, true });
    }

    [Fact]
    public void TryGetWindowStyle_ZeroStyleWithStaleLastError_IsSuccessful()
    {
        var window = CreateWindowEx(
            0,
            "STATIC",
            null,
            0,
            0,
            0,
            0,
            0,
            (nint)(-3),
            0,
            0,
            0);
        Assert.NotEqual(0, window);

        try
        {
            var previousStyle = SetWindowLongPtr(window, -16, 0);
            Assert.NotEqual(0, previousStyle);
            Marshal.SetLastSystemError(5);

            var succeeded = ForegroundFullscreenNative.Instance.TryGetWindowStyle(
                window,
                out var style);

            Assert.True(succeeded);
            Assert.Equal(0u, style);
        }
        finally
        {
            DestroyWindow(window);
        }
    }

    private static QueueForegroundFullscreenNative StableFullscreenNative() => new()
    {
        FrameBounds = FullHd,
        MonitorBounds = FullHd
    };

    private static void QueueStableAttempts(
        QueueForegroundFullscreenNative native,
        long window,
        int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            QueueForeground(native, window, window);
        }
    }

    private static void QueueForeground(
        QueueForegroundFullscreenNative native,
        params long[] windows)
    {
        foreach (var window in windows)
        {
            native.ForegroundWindows.Enqueue((nint)window);
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    public enum ExcludedWindowKind
    {
        Desktop,
        Shell,
        Invisible,
        Minimized,
        Child,
        Cloaked
    }

    public enum QueryFailure
    {
        Cloak,
        Frame,
        MonitorBounds
    }

    public enum InvalidRectangle
    {
        Frame,
        Monitor
    }

    private sealed class QueueForegroundFullscreenNative : IForegroundFullscreenNative
    {
        public Queue<nint> ForegroundWindows { get; } = new();
        public int ForegroundReadCount { get; private set; }
        public nint DesktopWindow { get; set; } = (nint)(-1);
        public nint ShellWindow { get; set; } = (nint)(-2);
        public bool IsWindowResult { get; set; } = true;
        public bool IsWindowVisibleResult { get; set; } = true;
        public bool IsWindowMinimizedResult { get; set; }
        public bool TryGetWindowStyleResult { get; set; } = true;
        public uint WindowStyle { get; set; }
        public bool TryGetCloakedResult { get; set; } = true;
        public bool Cloaked { get; set; }
        public bool TryGetExtendedFrameBoundsResult { get; set; } = true;
        public NativePixelRect FrameBounds { get; set; }
        public nint Monitor { get; set; } = (nint)1;
        public bool TryGetMonitorBoundsResult { get; set; } = true;
        public NativePixelRect MonitorBounds { get; set; }

        public nint GetForegroundWindow()
        {
            ForegroundReadCount++;
            return ForegroundWindows.Dequeue();
        }

        public nint GetDesktopWindow() => DesktopWindow;

        public nint GetShellWindow() => ShellWindow;

        public bool IsWindow(nint window) => IsWindowResult;

        public bool IsWindowVisible(nint window) => IsWindowVisibleResult;

        public bool IsWindowMinimized(nint window) => IsWindowMinimizedResult;

        public bool TryGetWindowStyle(nint window, out uint style)
        {
            style = WindowStyle;
            return TryGetWindowStyleResult;
        }

        public bool TryGetCloaked(nint window, out bool cloaked)
        {
            cloaked = Cloaked;
            return TryGetCloakedResult;
        }

        public bool TryGetExtendedFrameBounds(nint window, out NativePixelRect bounds)
        {
            bounds = FrameBounds;
            return TryGetExtendedFrameBoundsResult;
        }

        public nint GetIntersectingMonitor(nint window) => Monitor;

        public bool TryGetMonitorBounds(nint monitor, out NativePixelRect bounds)
        {
            bounds = MonitorBounds;
            return TryGetMonitorBoundsResult;
        }
    }
}
