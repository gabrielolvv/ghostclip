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
    /// <summary>Oldest first, by the time in the name; a clip renamed by hand goes before those, by name.</summary>
    public static IReadOnlyList<string> InOrder(IEnumerable<string> paths) =>
    [
        .. paths.OrderBy(p => ClipLibrary.TimeFromName(Path.GetFileName(p)) ?? DateTime.MinValue).ThenBy(p => p, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>The first clip that can't follow the first one, or null when they all can.</summary>
    public static JoinSource? FirstMismatch(IReadOnlyList<JoinSource> sources) =>
        sources.FirstOrDefault(s => s.Video != sources[0].Video || !s.Audio.SequenceEqual(sources[0].Audio));
}
