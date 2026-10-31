using System.Text.RegularExpressions;

namespace Ghostclip.Core;

/// <summary>
/// Keeps the clips folder under <see cref="GhostclipConfig.StorageLimitGB"/> by
/// deleting the oldest clips and screenshots. Careful by construction, since the
/// clips folder can be Videos or Downloads:
/// <list type="bullet">
/// <item>Only files named the way Ghostclip names them, in that game's folder (root\Game\Game_time),
/// count or are deleted. A shared folder's other files don't, even with a date in their name
/// (Telegram's video_2026-10-01_20-11-05.mp4), and a clip people renamed is theirs to keep.</item>
/// <item>Favorites are never deleted. They still count, so a limit smaller than them deletes everything else.</item>
/// <item>Age is when the file was written, not the time in its name: a session is named after its
/// start and a trim after its clip, yet both were just made.</item>
/// <item>The newest is always kept: a clip saved a second ago is never what makes room.</item>
/// </list>
/// </summary>
public static class StorageLimit
{
    public const long BytesPerGB = 1024L * 1024 * 1024;

    /// <summary>Whether <paramref name="clip"/> counts toward the limit: Ghostclip named it, in its game's folder.</summary>
    public static bool Counts(string root, ClipEntry clip)
    {
        var parts = Path.GetRelativePath(root, clip.Path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        // Game_<time>, then a number when two saves shared a second, then any _trim/_discord copies of copies.
        return parts.Length == 2 && Regex.IsMatch(Path.GetFileNameWithoutExtension(parts[1]),
            "^" + Regex.Escape(parts[0]) + @"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d+)?((_trim|_discord)(_\d+)?)*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>What the clips counted toward the limit take now.</summary>
    public static long Used(string root, IEnumerable<ClipEntry> clips) =>
        clips.Where(c => Counts(root, c)).Sum(c => c.Bytes);

    /// <summary>
    /// The clips to delete, oldest first, to get under <paramref name="limitBytes"/>;
    /// none when there is no limit (0) or the folder is under it.
    /// </summary>
    public static IReadOnlyList<ClipEntry> SelectForDeletion(string root, IReadOnlyList<ClipEntry> clips, LibraryState state, long limitBytes,
        DateTime? nowUtc = null)
    {
        if (limitBytes <= 0) return [];
        // A file dated in the future (a wrong clock, copied from another PC) counts as written now, so it can't
        // stay "newest" forever and leave the clip just saved unprotected.
        var now = nowUtc ?? DateTime.UtcNow;
        var counted = clips.Where(c => Counts(root, c))
            .OrderBy(c => c.LastWriteUtc > now ? now : c.LastWriteUtc).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var over = counted.Sum(c => c.Bytes) - limitBytes;
        var result = new List<ClipEntry>();
        foreach (var clip in counted.SkipLast(1))
        {
            if (over <= 0) break;
            if (state.IsFavorite(clip.Path)) continue;
            result.Add(clip);
            over -= clip.Bytes;
        }
        return result;
    }
}
