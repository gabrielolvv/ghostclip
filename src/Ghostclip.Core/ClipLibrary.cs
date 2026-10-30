using System.Globalization;
using System.Text.RegularExpressions;

namespace Ghostclip.Core;

/// <summary>One saved clip as the library shows it.</summary>
/// <param name="RelativeUrl">Path under the clips folder, escaped per segment, for the library's virtual host.</param>
/// <param name="Derived">A _trim or _discord copy of another clip.</param>
public sealed record ClipEntry(string Path, string RelativeUrl, string Game, DateTime SavedLocal, long Bytes, bool Derived);

/// <summary>
/// The clips folder is the library: one folder per game, as saving lays it
/// out. No database; the library window scans it when it opens.
/// </summary>
public static partial class ClipLibrary
{
    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>Every clip under <paramref name="root"/>, newest first. A missing folder is an empty library.</summary>
    public static IReadOnlyList<ClipEntry> Scan(string root)
    {
        if (!Directory.Exists(root)) return [];
        var clips = new List<ClipEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*", Recursive))
        {
            // Exact extension: a clip being written is still a .mp4.part.
            if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase)) continue;
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
            clips.Add(new ClipEntry(path, ToRelativeUrl(root, path), game, TimeFromName(name) ?? info.LastWriteTime, info.Length, IsDerived(name)));
        }
        clips.Sort((a, b) => b.SavedLocal != a.SavedLocal
            ? b.SavedLocal.CompareTo(a.SavedLocal)
            : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return clips;
    }

    /// <summary>The save time in Game_YYYY-MM-DD_HH-MM-SS[_n][_trim|_discord[_n]].mp4, or null.</summary>
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
