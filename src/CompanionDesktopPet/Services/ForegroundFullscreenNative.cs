using System.Runtime.InteropServices;

namespace CompanionDesktopPet.Services;

internal readonly record struct NativePixelRect(int Left, int Top, int Right, int Bottom);

internal interface IForegroundFullscreenNative
{
    nint GetForegroundWindow();
    nint GetDesktopWindow();
    nint GetShellWindow();
    bool IsWindow(nint window);
    bool IsWindowVisible(nint window);
    bool IsWindowMinimized(nint window);
    bool TryGetWindowStyle(nint window, out uint style);
    bool TryGetCloaked(nint window, out bool cloaked);
    bool TryGetExtendedFrameBounds(nint window, out NativePixelRect bounds);
    nint GetIntersectingMonitor(nint window);
    bool TryGetMonitorBounds(nint monitor, out NativePixelRect bounds);
}

internal sealed class ForegroundFullscreenNative : IForegroundFullscreenNative
{
    private const int GwlStyle = -16;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint MonitorDefaultToNull = 0;

    internal static ForegroundFullscreenNative Instance { get; } = new();

    private ForegroundFullscreenNative()
    {
    }

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public nint GetDesktopWindow() => NativeMethods.GetDesktopWindow();

    public nint GetShellWindow() => NativeMethods.GetShellWindow();

    public bool IsWindow(nint window) => NativeMethods.IsWindow(window);

    public bool IsWindowVisible(nint window) => NativeMethods.IsWindowVisible(window);

    public bool IsWindowMinimized(nint window) => NativeMethods.IsIconic(window);

    public bool TryGetWindowStyle(nint window, out uint style)
    {
        Marshal.SetLastSystemError(0);
        var value = NativeMethods.GetWindowLongPtr(window, GwlStyle);
        var error = Marshal.GetLastPInvokeError();
        if (value == 0 && error != 0)
        {
            style = 0;
            return false;
        }

        style = unchecked((uint)value.ToInt64());
        return true;
    }

    public bool TryGetCloaked(nint window, out bool cloaked)
    {
        var hresult = NativeMethods.DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            out int value,
            (uint)Marshal.SizeOf<int>());
        cloaked = value != 0;
        return hresult == 0;
    }

    public bool TryGetExtendedFrameBounds(nint window, out NativePixelRect bounds)
    {
        var hresult = NativeMethods.DwmGetWindowAttribute(
            window,
            DwmwaExtendedFrameBounds,
            out NativeRect value,
            (uint)Marshal.SizeOf<NativeRect>());
        bounds = Map(value);
        return hresult == 0;
    }

    public nint GetIntersectingMonitor(nint window) =>
        NativeMethods.MonitorFromWindow(window, MonitorDefaultToNull);

    public bool TryGetMonitorBounds(nint monitor, out NativePixelRect bounds)
    {
        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            bounds = default;
            return false;
        }

        bounds = Map(info.Monitor);
        return true;
    }

    private static NativePixelRect Map(NativeRect value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern nint GetDesktopWindow();

        [DllImport("user32.dll")]
        internal static extern nint GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(nint window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern nint GetWindowLongPtr(nint window, int index);

        [DllImport("user32.dll")]
        internal static extern nint MonitorFromWindow(nint window, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        internal static extern int DwmGetWindowAttribute(
            nint window,
            int attribute,
            out NativeRect value,
            uint valueSize);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        internal static extern int DwmGetWindowAttribute(
            nint window,
            int attribute,
            out int value,
            uint valueSize);
    }
}
