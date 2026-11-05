using Ghostclip.Core;
using Xunit;

namespace Ghostclip.Core.Tests;

public class SaveFrameTests
{
    [Fact]
    public void Frame_seeks_before_the_input_and_writes_one_good_jpeg()
    {
        var args = FfmpegArgs.Frame("in.mp4", "out.jpg.part", 2.5).ToList();
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.Equal("2.500000", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("1", args[args.IndexOf("-frames:v") + 1]);
        Assert.Equal("mjpeg", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("2", args[args.IndexOf("-q:v") + 1]);
        Assert.Equal("image2", args[args.IndexOf("-f") + 1]);
        Assert.Equal("out.jpg.part", args[^1]);
    }

    [Fact]
    public void Frame_time_uses_a_point_whatever_the_language_of_Windows()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var args = FfmpegArgs.Frame("in.mp4", "out.jpg", 1.5).ToList();
            Assert.Equal("1.500000", args[args.IndexOf("-ss") + 1]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    // FFmpeg drops everything before the time it's given, and the player shows the frame it has reached:
    // the time goes a little before the frame on screen, so that is the first one kept.
    [Theory]
    [InlineData(1.0, 60, 60)]       // Exactly on frame 60.
    [InlineData(1.01, 60, 60)]      // Between 60 and 61: 60 is on screen.
    [InlineData(1.0166, 60, 60)]    // Just short of 61.
    [InlineData(1.01667, 60, 61)]   // Just past it.
    [InlineData(0.05, 60, 3)]       // A time that floating point keeps just under frame 3.
    [InlineData(2.0, 30, 60)]
    public void Frame_time_lands_just_before_the_frame_on_screen(double at, double fps, int frame)
    {
        var seek = FfmpegArgs.FrameSeek(at, fps);
        Assert.True(seek < frame / fps, $"{seek} should be before frame {frame}");
        Assert.True(seek > (frame - 1) / fps, $"{seek} should be after frame {frame - 1}");
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(0.001, 60)]
    [InlineData(-3, 60)]
    public void Frame_time_never_goes_before_the_clip(double at, double fps) =>
        Assert.Equal(0, FfmpegArgs.FrameSeek(at, fps));

    [Theory]
    [InlineData(2.5, 0)]
    [InlineData(2.5, -1)]
    public void Frame_time_is_as_given_when_the_frame_rate_is_unknown(double at, double fps) =>
        Assert.Equal(at, FfmpegArgs.FrameSeek(at, fps));

    [Fact]
    public void Frame_goes_next_to_its_clip_and_several_can_be_saved()
    {
        var clip = Path.Combine("G", "G_2026-10-30_21-14-02.mp4");
        var first = Path.Combine("G", "G_2026-10-30_21-14-02_frame.jpg");
        Assert.Equal(first, ClipNaming.DerivedPath(clip, "frame", _ => false, ".jpg"));
        Assert.Equal(Path.Combine("G", "G_2026-10-30_21-14-02_frame_2.jpg"), ClipNaming.DerivedPath(clip, "frame", p => p == first, ".jpg"));
    }

    [Theory]
    [InlineData("G_2026-10-30_21-14-02_frame.jpg")]
    [InlineData("G_2026-10-30_21-14-02_frame_2.jpg")]
    [InlineData("G_2026-10-30_21-14-02_trim_frame.jpg")]
    public void Frame_counts_as_made_by_Ghostclip_and_keeps_the_clips_date(string name)
    {
        Assert.True(ClipLibrary.IsGhostclipName("G", Path.GetFileNameWithoutExtension(name)));
        Assert.Equal(new DateTime(2026, 10, 30, 21, 14, 2), ClipLibrary.TimeFromName(name));
    }
}
