namespace Ghostclip.Core;

public static class FfmpegLocator
{
    /// <summary>
    /// Finds ffmpeg.exe: the configured path, then ffmpeg\ next to the app (how
    /// it ships), then a tools\ffmpeg folder above the app (a dev checkout), then PATH.
    /// </summary>
    public static string? Find(string? configured, string appDir)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        var bundled = Path.Combine(appDir, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;

        for (var dir = new DirectoryInfo(appDir); dir is not null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName, "tools", "ffmpeg");
            if (Directory.Exists(tools))
            {
                var found = Directory.EnumerateFiles(tools, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (entry.Length == 0) continue;
            var candidate = Path.Combine(entry.Trim('"'), "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
