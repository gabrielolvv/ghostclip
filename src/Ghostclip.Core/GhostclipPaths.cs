namespace Ghostclip.Core;

/// <summary>
/// Where Ghostclip keeps its files: %LOCALAPPDATA%\GhostclipData. The installer
/// owns %LOCALAPPDATA%\Ghostclip and wipes it on a fresh install, so data lives
/// next to it rather than inside it; the uninstaller removes both.
/// </summary>
public sealed record GhostclipPaths(string DataDir)
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string InstallRoot { get; } = Path.Combine(LocalAppData, "Ghostclip");

    public static GhostclipPaths Default { get; } = new(Path.Combine(LocalAppData, "GhostclipData"));

    /// <summary>Where 0.4.0 kept its data, inside the install folder. Builds before it used the install root itself.</summary>
    public static string OldDataDir { get; } = Path.Combine(InstallRoot, "Data");

    public string BufferDir => Path.Combine(DataDir, "Buffer");

    /// <summary>Hard-linked snapshots waiting to be remuxed. Survives a crash mid-save.</summary>
    public string PendingDir => Path.Combine(DataDir, "Pending");

    public string LogsDir => Path.Combine(DataDir, "Logs");

    public string ConfigFile => Path.Combine(DataDir, "config.json");

    public string ProbeFile => Path.Combine(DataDir, "probe.json");

    public static string DefaultClipsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Ghostclip");

    /// <summary>
    /// Brings over what an earlier version left in <paramref name="oldDir"/>:
    /// config.json and clips still waiting to be written. A dedicated old data
    /// folder is then deleted (its buffer alone can be hundreds of MB); the
    /// install root itself is never deleted, since the installer owns it.
    /// </summary>
    public void MigrateFrom(string oldDir, bool deleteOld)
    {
        if (!Directory.Exists(oldDir) || string.Equals(Path.GetFullPath(oldDir), Path.GetFullPath(DataDir), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var oldConfig = Path.Combine(oldDir, "config.json");
        if (File.Exists(oldConfig) && !File.Exists(ConfigFile))
        {
            Directory.CreateDirectory(DataDir);
            File.Move(oldConfig, ConfigFile);
        }
        var oldPending = Path.Combine(oldDir, "Pending");
        if (Directory.Exists(oldPending))
        {
            Directory.CreateDirectory(PendingDir);
            foreach (var snapshot in Directory.EnumerateDirectories(oldPending))
            {
                var target = Path.Combine(PendingDir, Path.GetFileName(snapshot));
                if (!Directory.Exists(target)) Directory.Move(snapshot, target);
            }
        }
        if (deleteOld)
        {
            Directory.Delete(oldDir, recursive: true);
        }
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BufferDir);
        Directory.CreateDirectory(PendingDir);
        Directory.CreateDirectory(LogsDir);
    }
}
