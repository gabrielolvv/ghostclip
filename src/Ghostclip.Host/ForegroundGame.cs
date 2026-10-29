using System.Diagnostics;
using Ghostclip.Core;

namespace Ghostclip.Host;

/// <summary>
/// Names the clip after the program in the foreground. Reads only the window's
/// process id and the version info of its .exe on disk.
/// </summary>
internal static class ForegroundGame
{
    public static unsafe string Detect()
    {
        var window = Native.GetForegroundWindow();
        if (window == 0) return ClipNaming.FallbackGame;
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return ClipNaming.FallbackGame;

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
            return GameNameResolver.Resolve(Path.GetFileNameWithoutExtension(path), info.ProductName, info.FileDescription);
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
}
