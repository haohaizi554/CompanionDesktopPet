using System.Runtime.InteropServices;

namespace CompanionDesktopPet.Services;

public interface IIdleTimeProvider
{
    TimeSpan? GetIdleTime();
}

public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    public TimeSpan? GetIdleTime()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        var currentTick = unchecked((uint)Environment.TickCount64);
        var elapsedMilliseconds = unchecked(currentTick - info.LastInputTick);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint LastInputTick;
    }
}
