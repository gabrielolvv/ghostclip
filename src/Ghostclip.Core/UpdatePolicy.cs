namespace Ghostclip.Core;

/// <summary>
/// When a downloaded update installs on its own. Velopack's installer stops every
/// Ghostclip process (tray, worker, FFmpeg, library) and restarts only the one
/// that started it, so it must never be started by a side process while the tray
/// is recording.
/// </summary>
public static class UpdatePolicy
{
    /// <summary>Only a plain launch (the tray, at sign-in or from the Start menu) installs a pending update first.</summary>
    public static bool ApplyOnStartup(IReadOnlyList<string> args) => args.Count == 0;

    /// <summary>On Exit from the tray menu, unless the library is open: installing would close it mid-work.</summary>
    public static bool ApplyOnExit(bool updateReady, bool libraryOpen) => updateReady && !libraryOpen;
}
