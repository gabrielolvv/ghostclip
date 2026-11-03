using System.Runtime.InteropServices;

namespace Ghostclip.Worker.Native;

/// <summary>The game's window as the tray reported it, checked before capturing it.</summary>
internal static partial class GameWindow
{
    /// <summary>The window when it still exists; 0 (the whole monitor) when the game closed it.</summary>
    public static long IfAlive(long window) => window != 0 && IsWindow((nint)window) ? window : 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);
}
