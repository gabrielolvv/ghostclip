namespace Ghostclip.Core;

/// <summary>Where Ghostclip keeps its files. Everything lives under one data folder.</summary>
public sealed record GhostclipPaths(string DataDir)
{
    public static GhostclipPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostclip"));

    public string BufferDir => Path.Combine(DataDir, "Buffer");

    /// <summary>Hard-linked snapshots waiting to be remuxed. Survives a crash mid-save.</summary>
    public string PendingDir => Path.Combine(DataDir, "Pending");

    public string LogsDir => Path.Combine(DataDir, "Logs");

    public string ConfigFile => Path.Combine(DataDir, "config.json");

    public string ProbeFile => Path.Combine(DataDir, "probe.json");

    public static string DefaultClipsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Ghostclip");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BufferDir);
        Directory.CreateDirectory(PendingDir);
        Directory.CreateDirectory(LogsDir);
    }
}
