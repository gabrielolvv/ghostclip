using System.Runtime.InteropServices;

namespace Ghostclip.Worker.Native;

/// <summary>
/// Whether Desktop Duplication can run: the session must be unlocked (Windows
/// 11 keeps the lock screen on the normal desktop, so only the session state
/// tells) and the input desktop must be the user's, not the secure desktop of
/// a UAC prompt or Ctrl+Alt+Del.
/// </summary>
internal static partial class InputDesktop
{
    private const uint DESKTOP_SWITCHDESKTOP = 0x0100;
    private const int WTSSessionInfoEx = 25;
    private const int WTS_CURRENT_SESSION = -1;
    private const int WTS_SESSIONSTATE_LOCK = 0;

    public static bool IsAvailable() => !SessionLocked() && InputDesktopIsOurs();

    private static unsafe bool SessionLocked()
    {
        if (!WTSQuerySessionInformationW(0, WTS_CURRENT_SESSION, WTSSessionInfoEx, out var buffer, out var bytes)) return false;
        try
        {
            // WTSINFOEXW: DWORD Level, then WTSINFOEX_LEVEL1_W { SessionId, SessionState, SessionFlags, ... }.
            return bytes >= 16 && *(int*)buffer == 1 && *(int*)(buffer + 12) == WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static bool InputDesktopIsOurs()
    {
        var desktop = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (desktop == 0) return false;
        CloseDesktop(desktop);
        return true;
    }

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformationW(nint server, int sessionId, int infoClass, out nint buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);
}
