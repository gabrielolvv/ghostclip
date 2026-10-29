using System.Globalization;

namespace Ghostclip.Core;

public readonly record struct SegmentFile(string Path, long Sequence, long Length, DateTime LastWriteUtc);

/// <summary>
/// Rotation of the on-disk replay buffer.
///
/// Segments get ever-increasing numbers (seg_00000042.ts) instead of FFmpeg's
/// -segment_wrap. Wrapping reuses a name by truncating the file in place, which
/// would also destroy any hard link a save took of it. With increasing numbers
/// the oldest file is deleted, which only removes its name, and a restarted
/// worker continues the sequence instead of overwriting the buffer.
/// </summary>
public static class SegmentRing
{
    public const string FfmpegPattern = "seg_%08d.ts";
    private const string Prefix = "seg_";
    private const string Extension = ".ts";

    public static string FileName(long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{sequence:D8}{Extension}");

    public static bool TryParseSequence(string fileName, out long sequence)
    {
        sequence = 0;
        var name = System.IO.Path.GetFileName(fileName);
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var digits = name.AsSpan(Prefix.Length, name.Length - Prefix.Length - Extension.Length);
        return digits.Length > 0 &&
            long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }

    /// <summary>Number for the first segment of a new recording session.</summary>
    public static long NextStartNumber(IEnumerable<SegmentFile> existing)
    {
        long max = -1;
        foreach (var s in existing)
        {
            max = Math.Max(max, s.Sequence);
        }
        return max + 1;
    }

    /// <summary>
    /// Segments to keep on disk: enough for a full clip, plus the one being
    /// written, plus slack so a save in flight never loses its oldest segment.
    /// </summary>
    public static int KeepCount(int clipSeconds, int segmentSeconds) =>
        (int)Math.Ceiling(clipSeconds / (double)segmentSeconds) + 3;

    /// <summary>The oldest segments beyond <paramref name="keep"/>.</summary>
    public static IReadOnlyList<SegmentFile> SelectForDeletion(IEnumerable<SegmentFile> existing, int keep) =>
        existing.OrderByDescending(s => s.Sequence).Skip(Math.Max(0, keep)).ToList();

    /// <summary>Segments too old to belong in any clip (left by a session that ended long ago).</summary>
    public static IReadOnlyList<SegmentFile> SelectStale(IEnumerable<SegmentFile> existing, DateTime nowUtc, TimeSpan maxAge) =>
        existing.Where(s => nowUtc - s.LastWriteUtc > maxAge).ToList();

    /// <summary>Lists the buffer, ordered by sequence. Other files are ignored.</summary>
    public static IReadOnlyList<SegmentFile> Scan(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }
        var result = new List<SegmentFile>();
        foreach (var info in new DirectoryInfo(directory).EnumerateFiles("seg_*.ts"))
        {
            if (TryParseSequence(info.Name, out var sequence))
            {
                result.Add(new SegmentFile(info.FullName, sequence, info.Length, info.LastWriteTimeUtc));
            }
        }
        result.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        return result;
    }
}
