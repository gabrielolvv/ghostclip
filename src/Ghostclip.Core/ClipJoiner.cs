namespace Ghostclip.Core;

/// <param name="Video">The video track's codec, size and codec setup, as one text to compare.</param>
/// <param name="Audio">The same for each audio track, in order.</param>
public sealed record JoinSource(string Path, string Video, IReadOnlyList<string> Audio);

/// <summary>
/// Joining several clips into one by stream copy. It takes the same format throughout: MP4 keeps one codec setup
/// per track, so a clip from another encoder or with other settings would play as garbage from where it starts.
/// </summary>
public static class ClipJoiner
{
    /// <summary>Oldest first, as the library dates them: by the time in the name, or the file's for a clip renamed by hand.</summary>
    public static IReadOnlyList<string> InOrder(IEnumerable<string> paths, Func<string, DateTime> fileTime) =>
    [
        .. paths.OrderBy(p => ClipLibrary.TimeFromName(Path.GetFileName(p)) ?? fileTime(p)).ThenBy(p => p, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// Whether FFmpeg stopped reading a clip partway. It takes that for the end of the input and exits with 0, so the
    /// result would be short and look fine. -xerror isn't the answer: it also stops at the harmless overlap between
    /// two clips' audio, which FFmpeg smooths over.
    /// </summary>
    public static bool LostAClip(string ffmpegErrors) =>
        ffmpegErrors.Contains("Impossible to open", StringComparison.Ordinal) ||
        ffmpegErrors.Contains("Error during demuxing", StringComparison.Ordinal);

    /// <summary>The first clip that can't follow the first one, or null when they all can.</summary>
    public static JoinSource? FirstMismatch(IReadOnlyList<JoinSource> sources) =>
        sources.FirstOrDefault(s => s.Video != sources[0].Video || !s.Audio.SequenceEqual(sources[0].Audio));
}
