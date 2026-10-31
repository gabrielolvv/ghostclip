namespace Ghostclip.Core;

/// <summary>
/// Which replay-buffer segments a session recording takes. A session is cut into
/// parts at every recorder restart (a crash, a locked screen): segments of two
/// runs can differ in format and can't be joined, and a run after a long pause
/// may number its segments from 0 again.
/// </summary>
public static class SessionPlan
{
    /// <summary>
    /// Segments to link now, oldest first: from <paramref name="from"/> on, not linked yet, and below
    /// <paramref name="before"/> (where the next run starts, when a part ends at a restart). The one
    /// being written (the newest) waits until it closes, except at the end of a part.
    /// </summary>
    public static IReadOnlyList<long> ToLink(IReadOnlyList<long> sequencesAscending, long from, IReadOnlySet<long> linked, bool final,
        long before = long.MaxValue)
    {
        var writing = sequencesAscending.Count > 0 ? sequencesAscending[^1] : -1;
        return sequencesAscending.Where(s => s >= from && s < before && !linked.Contains(s) && (final || s != writing)).ToList();
    }

    /// <summary>
    /// Seconds into a part's joined file at <paramref name="nowUtc"/>: the segments before the one being
    /// written, plus how long that one has been written, from when the previous segment was last written
    /// (when this one began). File creation times can't be used: Windows gives a segment created under a
    /// recently deleted name that file's creation time (file system tunneling).
    /// </summary>
    public static double OffsetAt(long from, long writing, DateTime? previousClosedUtc, DateTime nowUtc, int segmentSeconds)
    {
        var whole = Math.Max(0, writing - from) * (double)segmentSeconds;
        var into = previousClosedUtc is { } begun ? Math.Clamp((nowUtc - begun).TotalSeconds, 0, segmentSeconds) : 0;
        return whole + into;
    }

    /// <summary>
    /// Where a part that began at <paramref name="afterUtc"/> starts: the oldest segment created since,
    /// whatever its number; null while the new run hasn't written one.
    /// </summary>
    public static long? FirstAfter(IEnumerable<(long Sequence, DateTime CreatedUtc)> segments, DateTime afterUtc)
    {
        long? first = null;
        foreach (var (sequence, created) in segments)
        {
            if (created >= afterUtc && (first is null || sequence < first)) first = sequence;
        }
        return first;
    }
}
