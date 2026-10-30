namespace Ghostclip.Core;

/// <summary>
/// Where Ghostclip keeps its files: %LOCALAPPDATA%\Ghostclip\Data. The installer
/// owns the rest of %LOCALAPPDATA%\Ghostclip (current\, packages\), so data sits
/// in its own subfolder and uninstalling removes both.
/// </summary>
public sealed record GhostclipPaths(string DataDir)
{
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostclip");

    public static GhostclipPaths Default { get; } = new(Path.Combine(InstallRoot, "Data"));

    public string BufferDir => Path.Combine(DataDir, "Buffer");

    /// <summary>Hard-linked snapshots waiting to be remuxed. Survives a crash mid-save.</summary>
    public string PendingDir => Path.Combine(DataDir, "Pending");

    public string LogsDir => Path.Combine(DataDir, "Logs");

    public string ConfigFile => Path.Combine(DataDir, "config.json");

    public string ProbeFile => Path.Combine(DataDir, "probe.json");

    public static string DefaultClipsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Ghostclip");

    /// <summary>Moves config.json from where builds before 0.2 kept it (the root folder).</summary>
    public void MigrateConfigFrom(string oldDir)
    {
        var old = Path.Combine(oldDir, "config.json");
        if (File.Exists(old) && !File.Exists(ConfigFile))
        {
            Directory.CreateDirectory(DataDir);
            File.Move(old, ConfigFile);
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
