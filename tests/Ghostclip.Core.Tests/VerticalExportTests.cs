using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A 9:16 copy of a clip for Shorts, TikTok and Reels: the middle of the picture, on the hardware encoder.
public class VerticalExportTests
{
    private static string VideoFilter(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-vf") + 1];

    private static string Option(IReadOnlyList<string> args, string name) => args[args.ToList().IndexOf(name) + 1];

    [Fact]
    public void It_keeps_the_middle_of_the_picture_at_1080_by_1920()
    {
        var filter = VideoFilter(FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", 1, 60));
        Assert.StartsWith("crop='trunc(min(iw,ih*9/16)/2)*2':'trunc(min(ih,iw*16/9)/2)*2',scale=1080:1920", filter);
        Assert.EndsWith("format=nv12", filter);
    }

    [Theory]
    [InlineData(60, false, "120")]
    [InlineData(30, false, "60")]
    [InlineData(144, true, "120")]
    [InlineData(0, false, "120")]
    public void Above_60_fps_it_comes_down_to_60(double sourceFps, bool capped, string gop)
    {
        var args = FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", 1, sourceFps);
        Assert.Equal(capped, VideoFilter(args).Contains("fps=60", StringComparison.Ordinal));
        Assert.Equal(gop, Option(args, "-g"));
    }

    [Fact]
    public void It_encodes_on_the_encoder_it_is_given_at_a_rate_the_platforms_keep()
    {
        var args = FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_qsv", 1, 60);
        Assert.Equal("h264_qsv", Option(args, "-c:v"));
        Assert.Equal("12000k", Option(args, "-b:v"));
        Assert.Equal("12000k", Option(args, "-maxrate"));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Theory]
    [InlineData(10, 15)]
    [InlineData(300, 15)]
    [InlineData(3600, 180)]
    public void A_long_session_gets_time_to_finish(double seconds, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), FfmpegArgs.VerticalTimeout(seconds));
    }

    [Fact]
    public void Several_audio_tracks_are_mixed_since_the_platforms_play_only_one()
    {
        var args = FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", 2, 60);
        Assert.Equal("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", Option(args, "-filter_complex"));
        Assert.Contains("[a]", args);
    }

    [Fact]
    public void One_audio_track_or_none_is_mapped_as_it_is()
    {
        var args = FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", 1, 60);
        Assert.DoesNotContain("-filter_complex", args);
        Assert.Contains("0:a:0?", args);
    }

    [Theory]
    [InlineData("G_2026-10-30_21-14-02_vertical")]
    [InlineData("G_2026-10-30_21-14-02_vertical_2")]
    [InlineData("G_2026-10-30_21-14-02_trim_vertical")]
    public void A_vertical_copy_counts_as_one_of_ghostclip_s_own_files(string stem)
    {
        Assert.True(ClipLibrary.IsGhostclipName("G", stem));
        Assert.Equal(new DateTime(2026, 10, 30, 21, 14, 2), ClipLibrary.TimeFromName(stem + ".mp4"));
    }

    [Fact]
    public void It_is_saved_next_to_the_clip_without_overwriting_another()
    {
        var clip = Path.Combine("G", "G_2026-10-30_21-14-02.mp4");
        var first = Path.Combine("G", "G_2026-10-30_21-14-02_vertical.mp4");
        Assert.Equal(first, ClipNaming.DerivedPath(clip, "vertical", _ => false));
        Assert.Equal(Path.Combine("G", "G_2026-10-30_21-14-02_vertical_2.mp4"), ClipNaming.DerivedPath(clip, "vertical", p => p == first));
    }
}
