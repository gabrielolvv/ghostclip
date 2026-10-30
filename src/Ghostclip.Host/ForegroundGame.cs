using System.Diagnostics;
using Ghostclip.Core;

namespace Ghostclip.Host;

/// <param name="Game">Name for the clip folder.</param>
/// <param name="ProcessId">0 for the desktop and other non-programs.</param>
/// <param name="Fullscreen">The window covers its whole monitor (fullscreen or borderless): most likely a game.</param>
internal readonly record struct Foreground(string Game, uint ProcessId, bool Fullscreen);

/// <summary>
/// Looks at the program in the foreground. Reads only the window's process id,
/// its rectangle, and the version info of its .exe on disk.
/// </summary>
internal static class ForegroundGame
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // Localized ("Sistema Operacional Microsoft Windows"...): read from the OS itself.
    private static readonly string? OsProductName = ReadOsProductName();

    // Polled every couple of seconds: the version resource is read once per process.
    private static uint _cachedPid;
    private static string _cachedName = ClipNaming.FallbackGame;

    public static Foreground Detect()
    {
        var window = Native.GetForegroundWindow();
        if (window == 0) return new(ClipNaming.FallbackGame, 0, false);
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return new(ClipNaming.FallbackGame, 0, false);
        if (pid != _cachedPid)
        {
            _cachedName = Resolve(pid);
            _cachedPid = pid;
        }
        // The shell and other non-games carry no process: nothing to capture audio from.
        return _cachedName == ClipNaming.FallbackGame
            ? new(_cachedName, 0, false)
            : new(_cachedName, pid, CoversMonitor(window));
    }

    /// <summary>Cheap liveness check, so game-only audio can fall back when the game closes.</summary>
    public static bool IsRunning(uint pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return false;
        try
        {
            return Native.GetExitCodeProcess(handle, out var code) && code == Native.STILL_ACTIVE;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static bool CoversMonitor(nint window)
    {
        if (!Native.GetWindowRect(window, out var r)) return false;
        var info = new Native.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static unsafe string Resolve(uint pid)
    {
        // Limited access works even for elevated or protected processes.
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return ByName(pid);
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            if (!Native.QueryFullProcessImageName(handle, 0, buffer, ref size)) return ByName(pid);
            var path = new string(buffer, 0, (int)size);
            var info = FileVersionInfo.GetVersionInfo(path);
            return GameNameResolver.Resolve(Path.GetFileNameWithoutExtension(path), info.ProductName, info.FileDescription, OsProductName);
        }
        catch (FileNotFoundException)
        {
            return ByName(pid);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static string ByName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return GameNameResolver.Resolve(p.ProcessName, null, null);
        }
        catch (ArgumentException)
        {
            return ClipNaming.FallbackGame;
        }
    }

    private static string? ReadOsProductName()
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")).ProductName;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
