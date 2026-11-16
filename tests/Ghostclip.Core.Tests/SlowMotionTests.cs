using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A slowed-down copy of the trim marks, at the speed the player is at, on the GPU encoder.
public class SlowMotionTests
{
    private static string Arg(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Theory]
    [InlineData(10, 15, 60, 10, 5)]
    [InlineData(0, 60, 60, 40, 20)]
    [InlineData(30, 10, 60, 40, 20)]
    [InlineData(0, 0, 8, 0, 8)]
    [InlineData(5, 100, 30, 10, 20)]
    public void Takes_the_marks_up_to_their_last_20_seconds(double start, double end, double duration, double from, double length)
    {
        var range = FfmpegArgs.SlowMotionRange(start, end, duration);
        Assert.Equal((from, length), range);
    }

    [Fact]
    public void Half_speed_stretches_the_picture_and_slows_the_sound()
    {
        var args = FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_nvenc", 12.5, 4, 0.5, audioTracks: 1);
        Assert.Equal("12.5", Arg(args, "-ss"));
        Assert.Equal("4", Arg(args, "-t"));
        Assert.Equal("setpts=2*PTS,format=nv12", Arg(args, "-vf"));
        Assert.Equal("[0:a:0]atempo=0.5[a]", Arg(args, "-filter_complex"));
        Assert.Equal("h264_nvenc", Arg(args, "-c:v"));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void A_quarter_speed_takes_two_halvings_of_the_sound()
    {
        var args = FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_qsv", 0, 4, 0.25, audioTracks: 1);
        Assert.Equal("setpts=4*PTS,format=nv12", Arg(args, "-vf"));
        Assert.Equal("[0:a:0]atempo=0.5,atempo=0.5[a]", Arg(args, "-filter_complex"));
    }

    [Fact]
    public void Several_tracks_are_mixed_and_then_slowed()
    {
        var args = FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_amf", 0, 4, 0.5, audioTracks: 2, volumes: [1, 0.5]);
        Assert.Equal("[0:a:1]volume=0.5[v1];[0:a:0][v1]amix=inputs=2:duration=first:normalize=0[m];[m]atempo=0.5[a]", Arg(args, "-filter_complex"));
        Assert.Contains("[a]", args);
    }

    [Fact]
    public void Every_track_off_makes_a_silent_copy()
    {
        var args = FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_nvenc", 0, 4, 0.5, audioTracks: 2, keep: []);
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-filter_complex", args);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0.1)]
    [InlineData(0)]
    public void Only_slower_speeds_down_to_a_quarter(double speed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_nvenc", 0, 4, speed, audioTracks: 1));

    [Theory]
    [InlineData("setpts")]
    [InlineData("atempo")]
    public void The_minimal_ffmpeg_has_the_filters_and_checks_them(string filter)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        var filters = flags.Single(l => l.StartsWith("--enable-filter=", StringComparison.Ordinal)).Split('=')[1].Split(',');
        Assert.Contains(filter, filters);
        Assert.Contains($"'{filter}'", File.ReadAllText(Path.Combine(dir.FullName, "build", "ffmpeg", "verify.ps1")));
    }

    [Fact]
    public void A_slow_copy_is_named_and_listed_as_a_copy()
    {
        const string name = "Elden Ring_2026-11-16_10-00-00_slow.mp4";
        Assert.True(ClipLibrary.IsDerived(name));
        Assert.True(ClipLibrary.IsGhostclipName("Elden Ring", Path.GetFileNameWithoutExtension(name)));
        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0), ClipLibrary.TimeFromName(name));
        Assert.True(ClipLibrary.IsDerived("Elden Ring_2026-11-16_10-00-00_trim_slow_2.mp4"));
    }
}
