using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Copies made on the GPU that can be trimmed or cut again: a keyframe every 2 s and no B-frames, as the recordings have.
public class CopyKeyframesTests
{
    private static string Arg(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void A_sped_up_copy_has_a_keyframe_every_2_seconds_at_the_clip_rate()
    {
        // The GOP alone gave no keyframe on a Radeon, and QSV kept its own without an IDR forced: all three, as the recorder.
        var args = FfmpegArgs.FastForward("in.mp4", "out.mp4", "h264_amf", 0, 300, 2, 60, audioTracks: 1);
        Assert.Equal("120", Arg(args, "-g"));
        Assert.Equal("0", Arg(args, "-bf"));
        Assert.Equal("expr:gte(t,n_forced*2)", Arg(args, "-force_key_frames"));
        Assert.Equal("1", Arg(args, "-forced_idr"));
    }

    [Fact]
    public void A_slowed_down_copy_counts_its_gop_in_its_own_slower_frames()
    {
        // 60 fps at half speed shows 30 frames a second.
        var args = FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_nvenc", 0, 4, 0.5, audioTracks: 1, fps: 60);
        Assert.Equal("60", Arg(args, "-g"));
        Assert.Equal("0", Arg(args, "-bf"));
        Assert.Equal("expr:gte(t,n_forced*2)", Arg(args, "-force_key_frames"));
        Assert.Equal("1", Arg(args, "-forced-idr"));
        Assert.DoesNotContain("-forced_idr", args);
    }

    [Fact]
    public void The_self_test_clip_has_a_keyframe_every_second()
    {
        // Its cut-out step takes out the second in the middle, which needs keyframes at 1 and 2 s on every GPU.
        var args = FfmpegArgs.SelfTestClip("out.mp4", "h264_qsv");
        Assert.Equal("30", Arg(args, "-g"));
        Assert.Equal("0", Arg(args, "-bf"));
        Assert.Equal("expr:gte(t,n_forced*1)", Arg(args, "-force_key_frames"));
        Assert.Equal("1", Arg(args, "-forced_idr"));
    }

    [Theory]
    [InlineData("Final_cut.mp4")]
    [InlineData("intro_muted.mp4")]
    [InlineData("Race_fast.mp4")]
    [InlineData("my play_trim.mp4")]
    public void A_name_of_ones_own_with_a_copy_word_at_the_end_is_not_a_copy(string name)
    {
        Assert.False(ClipLibrary.IsDerived(name));
    }
}
