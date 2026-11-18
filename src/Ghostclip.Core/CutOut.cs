namespace Ghostclip.Core;

/// <summary>
/// Cutting the marked part out of a clip and keeping what comes before and after it, by stream copy: no
/// re-encode, so the part after starts at a keyframe, the one at or before the end mark, as a trim does.
/// </summary>
public static class CutOut
{
    /// <summary>A stretch of the clip kept: from <paramref name="In"/>, up to <paramref name="Out"/> or to its end.</summary>
    public sealed record Part(double In, double? Out);

    /// <summary>
    /// How much earlier an out point goes in a clip with B-frames: the demuxer stops by decoding time, where a keyframe
    /// comes <paramref name="bFrames"/> frames ahead, so at the mark itself that keyframe, of the part cut out, would
    /// come in. Ghostclip's recordings and copies have none.
    /// </summary>
    public static double OutLead(int bFrames, double fps) => bFrames > 0 && fps > 0 ? bFrames / fps : 0;

    // Closer than this to the start or the end of the clip counts as at it.
    private const double Edge = 0.05;

    /// <summary>
    /// What stays of a clip of <paramref name="duration"/> seconds with <paramref name="start"/> to
    /// <paramref name="end"/> cut out. Empty when nothing would go, as a mark within one keyframe's stretch, or
    /// nothing would stay, as marks around all of it.
    /// </summary>
    public static IReadOnlyList<Part> Kept(double start, double end, double duration, IReadOnlyList<double> keyframes)
    {
        start = Math.Max(0, start);
        end = Math.Min(end, duration);
        if (end <= start) return [];
        var before = start > Edge;
        if (end >= duration - Edge) return before ? [new Part(0, start)] : [];
        var resume = ClipMedia.SnapStart(keyframes, end);
        if (resume <= start + Edge) return [];
        return before ? [new Part(0, start), new Part(resume, null)] : [new Part(resume, null)];
    }
}
