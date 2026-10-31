namespace Ghostclip.Core;

/// <summary>
/// Keeps the clips folder under <see cref="GhostclipConfig.StorageLimitGB"/> by
/// deleting the oldest clips and screenshots. Careful by construction, since the
/// clips folder can be Videos or Downloads:
/// <list type="bullet">
/// <item>Only files in a game's folder (root\Game\file) count; a shared folder's other videos don't.</item>
/// <item>Only files with the name Ghostclip gave them are deleted; one people renamed is theirs to keep.</item>
/// <item>Favorites are never deleted. They still count, so a limit smaller than them deletes everything else.</item>
/// <item>The newest is always kept: a clip saved a second ago is never what makes room.</item>
/// </list>
/// </summary>
public static class StorageLimit
{
    public const long BytesPerGB = 1024L * 1024 * 1024;

    /// <summary>Whether <paramref name="clip"/> counts toward the limit: it sits in a game's folder.</summary>
    public static bool Counts(string root, ClipEntry clip) =>
        Path.GetRelativePath(root, clip.Path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Length == 2;

    /// <summary>What the clips counted toward the limit take now.</summary>
    public static long Used(string root, IEnumerable<ClipEntry> clips) =>
        clips.Where(c => Counts(root, c)).Sum(c => c.Bytes);

    /// <summary>
    /// The clips to delete, oldest first, to get under <paramref name="limitBytes"/>;
    /// none when there is no limit (0) or the folder is under it.
    /// </summary>
    public static IReadOnlyList<ClipEntry> SelectForDeletion(string root, IReadOnlyList<ClipEntry> clips, LibraryState state, long limitBytes)
    {
        if (limitBytes <= 0) return [];
        var counted = clips.Where(c => Counts(root, c))
            .OrderBy(c => c.SavedLocal).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var over = counted.Sum(c => c.Bytes) - limitBytes;
        var result = new List<ClipEntry>();
        foreach (var clip in counted.SkipLast(1))
        {
            if (over <= 0) break;
            if (state.IsFavorite(clip.Path) || ClipLibrary.TimeFromName(Path.GetFileName(clip.Path)) is null) continue;
            result.Add(clip);
            over -= clip.Bytes;
        }
        return result;
    }
}
