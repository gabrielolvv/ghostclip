namespace Ghostclip.Core;

/// <summary>
/// Clips are written as "&lt;name&gt;.part" and moved to their name once complete,
/// so nothing (the library, Explorer, Discord) ever opens a half-written MP4.
/// </summary>
public static class PartFile
{
    public static string For(string output) => output + ".part";

    /// <summary>Moves the finished part file to <paramref name="output"/>. Never replaces an existing file.</summary>
    public static void Commit(string output) => File.Move(For(output), output, overwrite: false);

    /// <summary>
    /// Deletes Ghostclip's own .part files older than <paramref name="age"/>: a save, trim or
    /// compress that was cut off and will never be finished. Only where Ghostclip writes them
    /// (one folder per game under <paramref name="root"/>) and only with its file names, since
    /// the clips folder can be Downloads or Videos, where other programs keep their own
    /// .part files (a paused download). Returns how many.
    /// </summary>
    public static int CleanAbandoned(string root, TimeSpan age, DateTime nowUtc)
    {
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        foreach (var part in Directory.EnumerateDirectories(root, "*", options)
            .SelectMany(game => Directory.EnumerateFiles(game, "*.mp4.part", options)))
        {
            if (ClipLibrary.TimeFromName(Path.GetFileNameWithoutExtension(part)) is null) continue;
            try
            {
                if (nowUtc - File.GetLastWriteTimeUtc(part) < age) continue;
                File.Delete(part);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    /// <summary>
    /// Deletes the part file. Right after FFmpeg stops, it can stay locked for a moment
    /// (antivirus scanning the closed file), so this tries for about a second.
    /// </summary>
    public static void Discard(string output)
    {
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                File.Delete(For(output));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
