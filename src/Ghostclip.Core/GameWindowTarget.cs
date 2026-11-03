namespace Ghostclip.Core;

/// <summary>
/// Which window game-window capture records. It stays on the game it chose while that window is open: the tray
/// reports any fullscreen program as the game (a browser in F11, an overlay), and following each alt-tab would
/// restart recording every time. A closed window hands over to whatever is in front now; a minimized one records
/// the monitor until it is back, since Windows Graphics Capture gets no frames from it.
/// </summary>
public static class GameWindowTarget
{
    public readonly record struct WindowState(bool Alive, bool Minimized);

    /// <param name="preferred">The window chosen before (0: none).</param>
    /// <param name="reported">The game window the tray last reported (0: none).</param>
    /// <returns>The window to record now (0: the monitor), and the one to keep preferring.</returns>
    public static (long Target, long Preferred) Choose(long preferred, long reported, Func<long, WindowState> state)
    {
        if (preferred == 0 || !state(preferred).Alive)
        {
            preferred = reported != 0 && state(reported).Alive ? reported : 0;
        }
        var target = preferred != 0 && !state(preferred).Minimized ? preferred : 0;
        return (target, preferred);
    }
}
