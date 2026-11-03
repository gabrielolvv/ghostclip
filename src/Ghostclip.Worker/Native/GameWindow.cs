using System.Runtime.InteropServices;
using Ghostclip.Core;

namespace Ghostclip.Worker.Native;

/// <summary>The game's window as the tray reported it: whether it is still there, and its monitor's size.</summary>
internal static partial class GameWindow
{
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>
    /// Window and monitor sizes in physical pixels, as FFmpeg captures them, instead of the scaled ones a
    /// DPI-unaware process gets at 125 % or 150 %. The worker has no UI, so nothing else changes.
    /// </summary>
    public static void UsePhysicalPixels() => SetProcessDpiAwarenessContext(PerMonitorAwareV2);

    public static GameWindowTarget.WindowState State(long window) =>
        window != 0 && IsWindow((nint)window) ? new(true, IsIconic((nint)window)) : new(false, false);

    /// <summary>The size of the monitor the window is on; (0, 0) when it can't be read.</summary>
    public static (int Width, int Height) MonitorSize(long window)
    {
        var monitor = MonitorFromWindow((nint)window, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return (0, 0);
        return (info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(nint context);
}
