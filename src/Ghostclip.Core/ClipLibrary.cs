using System.Globalization;
using System.Text.RegularExpressions;

namespace Ghostclip.Core;

/// <summary>One saved clip or screenshot as the library shows it.</summary>
/// <param name="RelativeUrl">Path under the clips folder, escaped per segment, for the library's virtual host.</param>
/// <param name="Derived">A _trim or _discord copy of another clip.</param>
/// <param name="LastWriteUtc">With <paramref name="Bytes"/>, tells a replaced file from the one it replaced.</param>
/// <param name="Image">A screenshot rather than a video.</param>
public sealed record ClipEntry(string Path, string RelativeUrl, string Game, DateTime SavedLocal, long Bytes, bool Derived, DateTime LastWriteUtc, bool Image = false);

/// <summary>
/// The clips folder is the library: one folder per game, as saving lays it
/// out. No database; the library window scans it when it opens.
/// </summary>
public static partial class ClipLibrary
{
    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>Extensions the library lists: clips, and screenshots (.jpg from the hotkey, .png if people add them).</summary>
    public static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".png";

    /// <summary>Every clip and screenshot under <paramref name="root"/>, newest first. A missing folder is an empty library.</summary>
    public static IReadOnlyList<ClipEntry> Scan(string root)
    {
        if (!Directory.Exists(root)) return [];
        var clips = new List<ClipEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*", Recursive))
        {
            // Exact extension: a clip being written is still a .mp4.part.
            var image = IsImage(path);
            if (!image && !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase)) continue;
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                _ = info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            var relative = Path.GetRelativePath(root, path);
            var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
            var game = separator > 0 ? relative[..separator] : ClipNaming.FallbackGame;
            var name = info.Name;
            clips.Add(new ClipEntry(path, ToRelativeUrl(root, path), game, TimeFromName(name) ?? info.LastWriteTime, info.Length,
                !image && IsDerived(name), info.LastWriteTimeUtc, image));
        }
        clips.Sort((a, b) => b.SavedLocal != a.SavedLocal
            ? b.SavedLocal.CompareTo(a.SavedLocal)
            : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return clips;
    }

    /// <summary>
    /// Creates the clips folder if needed. False, with the reason, when it can't
    /// be used: a drive that isn't plugged in, a path without permission.
    /// </summary>
    public static bool TryPrepare(string root, out string? error)
    {
        try
        {
            Directory.CreateDirectory(root);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>The save time in Game_YYYY-MM-DD_HH-MM-SS[_n][_trim|_discord[_n]].mp4, or null.</summary>
    /// <summary>
    /// Whether <paramref name="stem"/> (a file name without extension) is one Ghostclip gives in the
    /// game folder <paramref name="folder"/>: Game_&lt;time&gt;, a number when two saves shared a second,
    /// then any _trim/_discord copies of copies. Telegram's video_&lt;time&gt; in its own folder is not.
    /// </summary>
    public static bool IsGhostclipName(string folder, string stem) =>
        Regex.IsMatch(stem, "^" + Regex.Escape(folder) + @"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d+)?((_trim|_discord)(_\d+)?)*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static DateTime? TimeFromName(string fileName)
    {
        var m = SavedName().Match(Path.GetFileNameWithoutExtension(fileName));
        return m.Success && DateTime.TryParseExact(m.Groups["time"].Value, "yyyy-MM-dd_HH-mm-ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }

    public static bool IsDerived(string fileName) => DerivedSuffix().IsMatch(Path.GetFileNameWithoutExtension(fileName));

    /// <summary>Whether <paramref name="path"/> lies under <paramref name="root"/>, after resolving "..".</summary>
    public static bool IsInside(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToRelativeUrl(string root, string path) =>
        string.Join('/', Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Select(Uri.EscapeDataString));

    [GeneratedRegex(@"_(?<time>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})(_\d+)?(_(trim|discord)(_\d+)?)?$")]
    private static partial Regex SavedName();

    [GeneratedRegex(@"_(trim|discord)(_\d+)?$")]
    private static partial Regex DerivedSuffix();
}
