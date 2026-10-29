namespace Ghostclip.Core;

/// <param name="Complete">Closed segments, oldest first.</param>
/// <param name="Current">The segment still being written, if the recorder is running.</param>
/// <param name="EstimatedSeconds">Approximate length of the resulting clip.</param>
public sealed record ClipPlan(IReadOnlyList<SegmentFile> Complete, SegmentFile? Current, double EstimatedSeconds)
{
    public bool IsEmpty => Complete.Count == 0 && Current is null;
}

public static class ClipPlanner
{
    /// <summary>
    /// Picks the segments that make up a clip of about <paramref name="clipSeconds"/>.
    ///
    /// Every segment starts on a keyframe, so the clip starts at the beginning of
    /// the oldest segment picked: the nearest keyframe at or before the requested
    /// start. The segment being written goes in too (a partial MPEG-TS is
    /// readable), so the clip runs up to the hotkey instead of up to 2 s before.
    /// </summary>
    public static ClipPlan Plan(IReadOnlyList<SegmentFile> segments, int clipSeconds, int segmentSeconds, bool recording)
    {
        var ordered = segments.Where(s => s.Length > 0).OrderBy(s => s.Sequence).ToList();
        if (ordered.Count == 0)
        {
            return new ClipPlan([], null, 0);
        }

        SegmentFile? current = null;
        double covered = 0;
        if (recording)
        {
            current = ordered[^1];
            ordered.RemoveAt(ordered.Count - 1);
            // The current segment started when the previous one was last written.
            covered = ordered.Count > 0
                ? Math.Clamp((current.Value.LastWriteUtc - ordered[^1].LastWriteUtc).TotalSeconds, 0, segmentSeconds)
                : segmentSeconds / 2.0;
        }

        var picked = new List<SegmentFile>();
        for (var i = ordered.Count - 1; i >= 0 && covered < clipSeconds; i--)
        {
            picked.Add(ordered[i]);
            covered += segmentSeconds;
        }
        picked.Reverse();
        return new ClipPlan(picked, current, covered);
    }
}
