using System.Globalization;
using System.Text.RegularExpressions;

namespace Ghostclip.Core;

/// <summary>One saved clip or screenshot as the library shows it.</summary>
/// <param name="RelativeUrl">Path under the clips folder, escaped per segment, for the library's virtual host.</param>
/// <param name="Derived">A _trim, _discord, _vertical, _joined, _slow, _fast, _cut or _muted copy of another clip.</param>
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

    /// <summary>Extensions the library lists: clips, and pictures (.jpg from the hotkey or a clip's frame, .gif from a clip, .png if people add them).</summary>
    public static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".png" or ".gif";

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

    /// <summary>The newest save in a scan, newest first: a copy made from it later, or a screenshot, isn't one.</summary>
    public static ClipEntry? LastClip(IReadOnlyList<ClipEntry> clips) => clips.FirstOrDefault(c => !c.Image && !c.Derived);

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

    /// <summary>
    /// Whether <paramref name="stem"/> (a file name without extension) is one Ghostclip gives in the
    /// game folder <paramref name="folder"/>: Game_&lt;time&gt;, a number when two saves shared a second,
    /// then any _trim/_discord/_vertical/_joined/_gif/_frame copies of copies. Telegram's video_&lt;time&gt; in its own folder is not.
    /// </summary>
    public static bool IsGhostclipName(string folder, string stem) =>
        Regex.IsMatch(stem, "^" + Regex.Escape(folder) + @"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d+)?((_trim|_discord|_vertical|_joined|_gif|_frame|_audio|_slow|_fast|_cut|_muted)(_\d+)?)*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The save time in Game_YYYY-MM-DD_HH-MM-SS[_n][_trim|_discord|_vertical|_joined|_gif|_frame[_n]]...., or null.</summary>
    public static DateTime? TimeFromName(string fileName)
    {
        var m = SavedName().Match(Path.GetFileNameWithoutExtension(fileName));
        return m.Success && DateTime.TryParseExact(m.Groups["time"].Value, "yyyy-MM-dd_HH-mm-ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }

    public static bool IsDerived(string fileName) => DerivedSuffix().IsMatch(Path.GetFileNameWithoutExtension(fileName));

    /// <summary>
    /// The clip a copy was made from: its name without the last _trim, _discord, _vertical, _slow, _fast, _cut, _muted, _gif or _frame,
    /// as an .mp4 in the same folder, when <paramref name="exists"/> says it's there. A copy of a copy leads to
    /// that copy. Null for a clip that isn't a copy, and for a joined one, which comes from several.
    /// </summary>
    public static string? SourceOf(string path, Func<string, bool> exists)
    {
        var m = CopyOf().Match(Path.GetFileNameWithoutExtension(path));
        if (!m.Success) return null;
        var source = Path.Combine(Path.GetDirectoryName(path) ?? "", m.Groups["source"].Value + ".mp4");
        return exists(source) ? source : null;
    }

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

    [GeneratedRegex(@"_(?<time>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})(_\d+)?(_(trim|discord|vertical|joined|gif|frame|audio|slow|fast|cut|muted)(_\d+)?)*$")]
    private static partial Regex SavedName();

    // After the save time, as Ghostclip names a copy: "Final_cut" of one's own is not one.
    [GeneratedRegex(@"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d+)?(_(trim|discord|vertical|joined|gif|frame|audio|slow|fast|cut|muted)(_\d+)?)*_(trim|discord|vertical|joined|slow|fast|cut|muted)(_\d+)?$")]
    private static partial Regex DerivedSuffix();

    [GeneratedRegex(@"^(?<source>.+)_(trim|discord|vertical|slow|fast|cut|muted|gif|frame)(_\d+)?$")]
    private static partial Regex CopyOf();
}
