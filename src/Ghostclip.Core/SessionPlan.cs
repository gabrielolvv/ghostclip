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
    /// Segments to link now, oldest first: from <paramref name="from"/> on, not linked yet. The one
    /// being written (the newest) waits until it closes, except at the end of a part.
    /// </summary>
    public static IReadOnlyList<long> ToLink(IReadOnlyList<long> sequencesAscending, long from, IReadOnlySet<long> linked, bool final)
    {
        var writing = sequencesAscending.Count > 0 ? sequencesAscending[^1] : -1;
        return sequencesAscending.Where(s => s >= from && !linked.Contains(s) && (final || s != writing)).ToList();
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
