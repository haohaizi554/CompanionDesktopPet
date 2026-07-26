namespace CompanionDesktopPet.Services;

internal sealed class WindowsForegroundFullscreenDetector : IForegroundFullscreenDetector
{
    private const int MaximumAttempts = 2;
    private const int EdgeTolerancePixels = 1;
    private const uint WsChild = 0x40000000u;
    private readonly IForegroundFullscreenNative _native;

    internal WindowsForegroundFullscreenDetector()
        : this(ForegroundFullscreenNative.Instance)
    {
    }

    internal WindowsForegroundFullscreenDetector(IForegroundFullscreenNative native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public bool? Observe(nint excludedWindow)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var start = _native.GetForegroundWindow();
            if (start == 0 || start == excludedWindow)
            {
                return null;
            }

            var result = Classify(start);
            var end = _native.GetForegroundWindow();
            if (end != start || result == ProbeResult.Retry)
            {
                continue;
            }

            return result == ProbeResult.Fullscreen;
        }

        return null;
    }

    private ProbeResult Classify(nint window)
    {
        if (window == _native.GetDesktopWindow() || window == _native.GetShellWindow())
        {
            return ProbeResult.NotFullscreen;
        }

        if (!_native.IsWindow(window))
        {
            return ProbeResult.Retry;
        }

        if (!_native.IsWindowVisible(window) || _native.IsWindowMinimized(window))
        {
            return ProbeResult.NotFullscreen;
        }

        if (!_native.TryGetWindowStyle(window, out var style))
        {
            return ProbeResult.Retry;
        }

        if ((style & WsChild) != 0)
        {
            return ProbeResult.NotFullscreen;
        }

        if (!_native.TryGetCloaked(window, out var cloaked))
        {
            return ProbeResult.Retry;
        }

        if (cloaked)
        {
            return ProbeResult.NotFullscreen;
        }

        if (!_native.TryGetExtendedFrameBounds(window, out var frame) || !IsPositive(frame))
        {
            return ProbeResult.Retry;
        }

        var monitor = _native.GetIntersectingMonitor(window);
        if (monitor == 0)
        {
            return ProbeResult.NotFullscreen;
        }

        if (!_native.TryGetMonitorBounds(monitor, out var bounds) || !IsPositive(bounds))
        {
            return ProbeResult.Retry;
        }

        return EdgesMatch(frame, bounds)
            ? ProbeResult.Fullscreen
            : ProbeResult.NotFullscreen;
    }

    private static bool IsPositive(NativePixelRect value) =>
        value.Right > value.Left && value.Bottom > value.Top;

    private static bool EdgesMatch(NativePixelRect frame, NativePixelRect monitor) =>
        Math.Abs((long)frame.Left - monitor.Left) <= EdgeTolerancePixels
        && Math.Abs((long)frame.Top - monitor.Top) <= EdgeTolerancePixels
        && Math.Abs((long)frame.Right - monitor.Right) <= EdgeTolerancePixels
        && Math.Abs((long)frame.Bottom - monitor.Bottom) <= EdgeTolerancePixels;

    private enum ProbeResult
    {
        Retry,
        NotFullscreen,
        Fullscreen
    }
}
