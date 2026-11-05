namespace Ghostclip.Core;

/// <summary>
/// What to cut from a session around its bookmarks. A bookmark is a press of the clip key, which also saved a clip
/// ending right there: these go on past it, for what happened next. Bookmarks close together make one clip.
/// </summary>
public static class BookmarkClips
{
    public const double SecondsAfter = 15;

    /// <param name="before">Seconds kept before each bookmark: the game's clip length.</param>
    /// <param name="keyframes">The session's keyframes, ascending. A cut copies, so it starts on one.</param>
    public static IReadOnlyList<(double Start, double End)> Ranges(IReadOnlyList<double> bookmarks, double duration, double before, IReadOnlyList<double> keyframes)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var at in bookmarks.Where(b => b >= 0 && b <= duration).Order())
        {
            var start = Math.Max(0, at - before);
            var end = Math.Min(duration, at + SecondsAfter);
            if (merged.Count > 0 && start <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else merged.Add((start, end));
        }
        return keyframes.Count == 0 ? merged : [.. merged.Select(r => (ClipMedia.SnapStart(keyframes, r.Start), r.End))];
    }
}
