using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A sped-up copy of the trim marks, to get through a long stretch of a session, on the GPU encoder.
public class FastForwardTests
{
    private static string Arg(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Theory]
    [InlineData(10, 15, 60, 10, 5)]
    [InlineData(0, 0, 8, 0, 8)]
    [InlineData(30, 10, 60, 0, 60)]
    [InlineData(100, 4000, 4000, 100, 1800)]
    [InlineData(0, 0, 4000, 0, 1800)]
    [InlineData(-5, 20, 60, 0, 20)]
    public void Takes_the_marks_up_to_their_first_30_minutes(double start, double end, double duration, double from, double length)
    {
        var range = FfmpegArgs.FastForwardRange(start, end, duration);
        Assert.Equal((from, length), range);
    }

    [Fact]
    public void Twice_the_speed_drops_every_other_frame_and_speeds_up_the_sound()
    {
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_nvenc", 12.5, 300, 2, 60, audioTracks: 1);
        Assert.Equal("12.5", Arg(args, "-ss"));
        Assert.Equal("300", Arg(args, "-t"));
        Assert.Equal("setpts=0.5*PTS,fps=60,format=nv12", Arg(args, "-vf"));
        Assert.Equal("[0:a:0]atempo=2[a]", Arg(args, "-filter_complex"));
        Assert.Equal("h264_nvenc", Arg(args, "-c:v"));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Four_times_takes_two_doublings_of_the_sound_and_keeps_the_clip_rate()
    {
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_qsv", 0, 300, 4, 30, audioTracks: 1);
        Assert.Equal("setpts=0.25*PTS,fps=30,format=nv12", Arg(args, "-vf"));
        Assert.Equal("[0:a:0]atempo=2,atempo=2[a]", Arg(args, "-filter_complex"));
    }

    [Fact]
    public void A_clip_rate_not_known_counts_as_60()
    {
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_qsv", 0, 300, 2, 0, audioTracks: 1);
        Assert.Equal("setpts=0.5*PTS,fps=60,format=nv12", Arg(args, "-vf"));
    }

    [Fact]
    public void Several_tracks_are_mixed_and_then_sped_up()
    {
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_amf", 0, 300, 2, 60, audioTracks: 2, volumes: [1, 0.5]);
        Assert.Equal("[0:a:1]volume=0.5[v1];[0:a:0][v1]amix=inputs=2:duration=first:normalize=0[m];[m]atempo=2[a]", Arg(args, "-filter_complex"));
    }

    [Fact]
    public void Even_volume_comes_before_the_speed()
    {
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_amf", 0, 300, 2, 60, audioTracks: 1, level: true);
        var graph = Arg(args, "-filter_complex");
        Assert.True(graph.IndexOf("loudnorm", StringComparison.Ordinal) < graph.IndexOf("atempo", StringComparison.Ordinal), graph);
    }

    [Fact]
    public void Every_track_off_makes_a_silent_copy()
    {
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_nvenc", 0, 300, 2, 60, audioTracks: 2, keep: []);
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-filter_complex", args);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.5)]
    [InlineData(5)]
    [InlineData(0)]
    public void Only_faster_speeds_up_to_four_times(double speed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_nvenc", 0, 300, speed, 60, audioTracks: 1));

    [Fact]
    public void A_fast_copy_is_named_and_listed_as_a_copy_of_its_clip()
    {
        const string name = "Elden Ring_2026-11-16_10-00-00_fast.mp4";
        Assert.True(ClipLibrary.IsDerived(name));
        Assert.True(ClipLibrary.IsGhostclipName("Elden Ring", Path.GetFileNameWithoutExtension(name)));
        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0), ClipLibrary.TimeFromName(name));
        Assert.True(ClipLibrary.IsDerived("Elden Ring_2026-11-16_10-00-00_trim_fast_2.mp4"));
        var source = Path.Combine("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4");
        Assert.Equal(source, ClipLibrary.SourceOf(Path.Combine("Elden Ring", name), p => p == source));
    }
}
