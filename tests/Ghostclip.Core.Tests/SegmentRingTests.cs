using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class SegmentRingTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);

    private static SegmentFile Seg(long seq, double atSeconds = 0, long length = 100) =>
        new($@"C:\buf\{SegmentRing.FileName(seq)}", seq, length, T0.AddSeconds(atSeconds));

    [Theory]
    [InlineData("seg_00000042.ts", 42)]
    [InlineData(@"C:\x\seg_00000000.ts", 0)]
    [InlineData("SEG_123.TS", 123)]
    public void Parses_sequence(string name, long expected)
    {
        Assert.True(SegmentRing.TryParseSequence(name, out var seq));
        Assert.Equal(expected, seq);
    }

    [Theory]
    [InlineData("seg_.ts")]
    [InlineData("seg_12a.ts")]
    [InlineData("seg_-1.ts")]
    [InlineData("clip_00000001.ts")]
    [InlineData("seg_00000001.mp4")]
    public void Rejects_other_files(string name) => Assert.False(SegmentRing.TryParseSequence(name, out _));

    [Fact]
    public void File_name_matches_ffmpeg_pattern() => Assert.Equal("seg_00000007.ts", SegmentRing.FileName(7));

    [Fact]
    public void Empty_buffer_starts_at_zero() => Assert.Equal(0, SegmentRing.NextStartNumber([]));

    [Fact]
    public void Restart_continues_after_newest_segment()
    {
        // A restarted worker must not overwrite what the buffer already holds.
        Assert.Equal(43, SegmentRing.NextStartNumber([Seg(40), Seg(42), Seg(41)]));
    }

    [Fact]
    public void Keep_count_covers_clip_plus_current_plus_slack()
    {
        Assert.Equal(33, SegmentRing.KeepCount(60, 2));
        Assert.Equal(19, SegmentRing.KeepCount(31, 2));
    }

    [Fact]
    public void Deletes_only_the_oldest_beyond_keep()
    {
        var segments = Enumerable.Range(0, 40).Select(i => Seg(i)).ToList();
        var doomed = SegmentRing.SelectForDeletion(segments, 33).Select(s => s.Sequence).OrderBy(x => x);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => (long)i), doomed);
    }

    [Fact]
    public void Deletes_nothing_when_under_keep() =>
        Assert.Empty(SegmentRing.SelectForDeletion([Seg(1), Seg(2)], 33));

    [Fact]
    public void Stale_segments_are_the_ones_older_than_max_age()
    {
        var now = T0.AddSeconds(500);
        var stale = SegmentRing.SelectStale([Seg(1, 0), Seg(2, 400), Seg(3, 499)], now, TimeSpan.FromSeconds(180));
        Assert.Equal([1L], stale.Select(s => s.Sequence));
    }

    [Fact]
    public void Scan_orders_by_sequence_and_ignores_other_files()
    {
        var dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;
        try
        {
            foreach (var name in new[] { "seg_00000010.ts", "seg_00000002.ts", "notes.txt", "seg_bad.ts" })
            {
                File.WriteAllText(Path.Combine(dir, name), "x");
            }
            Assert.Equal([2L, 10L], SegmentRing.Scan(dir).Select(s => s.Sequence));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Scan_of_missing_folder_is_empty() =>
        Assert.Empty(SegmentRing.Scan(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
}
