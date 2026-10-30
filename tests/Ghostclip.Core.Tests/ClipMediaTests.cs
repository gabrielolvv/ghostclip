using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class ClipMediaTests
{
    [Fact]
    public void Keyframes_come_from_packets_flagged_K()
    {
        Assert.Equal([0.0, 2.0], ClipMedia.ParseKeyframes("0.000000,K__\n0.016667,___\n2.000000,K__\r\nbad\n"));
    }

    [Theory]
    [InlineData(3.9, 2)]
    [InlineData(4, 4)]
    [InlineData(0.5, 0)]
    public void Start_snaps_to_the_keyframe_at_or_before_it(double seconds, double expected)
    {
        Assert.Equal(expected, ClipMedia.SnapStart([0, 2, 4], seconds));
    }

    [Fact]
    public void Start_without_keyframes_is_zero()
    {
        Assert.Equal(0, ClipMedia.SnapStart([], 5));
    }

    [Fact]
    public void Peaks_turn_decibels_into_linear_levels()
    {
        const string log = """
            [Parsed_ametadata_4 @ 000001] frame:0    pts:0       pts_time:0
            [Parsed_ametadata_4 @ 000001] lavfi.astats.Overall.Peak_level=-6.020600
            [Parsed_ametadata_4 @ 000001] lavfi.astats.Overall.Peak_level=-inf
            [Parsed_ametadata_4 @ 000001] lavfi.astats.Overall.Peak_level=0.000000
            """;
        var peaks = ClipMedia.ParsePeaks(log);
        Assert.Equal(3, peaks.Count);
        Assert.InRange(peaks[0], 0.499f, 0.501f);
        Assert.Equal(0f, peaks[1]);
        Assert.Equal(1f, peaks[2]);
    }

    [Fact]
    public void A_clip_without_audio_has_no_peaks()
    {
        Assert.Empty(ClipMedia.ParsePeaks(""));
    }

    [Theory]
    [InlineData(60, 1200)]
    [InlineData(0, 1)]
    public void Peaks_cover_the_clip_in_400_buckets(double seconds, int samples)
    {
        Assert.Equal(samples, ClipMedia.SamplesPerPeak(seconds));
    }

    [Fact]
    public void Cache_key_follows_size_and_time()
    {
        var t = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        var key = ClipMedia.CacheKey(@"C:\c\a.mp4", 10, t);
        Assert.Matches("^[0-9a-f]{16}$", key);
        Assert.Equal(key, ClipMedia.CacheKey(@"c:\C\A.mp4", 10, t));
        Assert.NotEqual(key, ClipMedia.CacheKey(@"C:\c\a.mp4", 11, t));
        Assert.NotEqual(key, ClipMedia.CacheKey(@"C:\c\a.mp4", 10, t.AddSeconds(1)));
    }

    [Fact]
    public void Thumbnail_seeks_before_the_input_and_writes_one_jpeg()
    {
        var args = FfmpegArgs.Thumbnail("in.mp4", "t.jpg", 20).ToList();
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.Equal("20.000", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("1", args[args.IndexOf("-frames:v") + 1]);
        Assert.Equal("image2", args[args.IndexOf("-f") + 1]);
        Assert.Equal("t.jpg", args[^1]);
    }

    [Fact]
    public void Peaks_read_the_first_audio_track_only()
    {
        var args = FfmpegArgs.AudioPeaks("in.mp4", 1200).ToList();
        Assert.Equal("0:a:0", args[args.IndexOf("-map") + 1]);
        Assert.Contains("asetnsamples=n=1200:p=0", args[args.IndexOf("-af") + 1]);
        Assert.Equal("null", args[args.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Keyframes_are_listed_from_packets_without_decoding()
    {
        var args = FfmpegArgs.Keyframes("in.mp4").ToList();
        Assert.Equal("packet=pts_time,flags", args[args.IndexOf("-show_entries") + 1]);
        Assert.Equal("v:0", args[args.IndexOf("-select_streams") + 1]);
        Assert.Equal("in.mp4", args[^1]);
    }
}
