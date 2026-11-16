using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The Discord size and vertical copies take the trim marks, as the GIF, slow motion and audio only do. Marks
// around the whole clip, which is how a clip opens, change nothing.
public class CopiesUseMarksTests
{
    private static readonly CompressionPlan Plan = new(6000, 128, 1080, 60);

    private static int At(IReadOnlyList<string> args, string flag) => args.ToList().IndexOf(flag);

    [Theory]
    [InlineData(5, 12, 30, 5, 7)]
    [InlineData(-2, 12, 30, 0, 12)]
    [InlineData(5, 40, 30, 5, 25)]
    public void Marks_inside_the_clip_are_a_part_of_it(double start, double end, double duration, double from, double length) =>
        Assert.Equal((from, length), FfmpegArgs.MarkedPart(start, end, duration));

    [Theory]
    [InlineData(0, 30, 30)]
    [InlineData(0.02, 29.97, 30)]
    [InlineData(0, 0, 30)]
    [InlineData(12, 5, 30)]
    [InlineData(5, 5, 30)]
    public void Marks_around_all_of_it_or_none_at_all_are_the_whole_clip(double start, double end, double duration) =>
        Assert.Null(FfmpegArgs.MarkedPart(start, end, duration));

    [Fact]
    public void The_discord_copy_seeks_to_the_part_before_reading()
    {
        var args = FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", 1, start: 5, length: 7);
        Assert.Equal("5", args[At(args, "-ss") + 1]);
        Assert.Equal("7", args[At(args, "-t") + 1]);
        Assert.True(At(args, "-t") < At(args, "-i"));
    }

    [Fact]
    public void The_vertical_copy_seeks_to_the_part_before_reading()
    {
        var args = FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_qsv", 1, 60, start: 12.5, length: 3.25);
        Assert.Equal("12.5", args[At(args, "-ss") + 1]);
        Assert.Equal("3.25", args[At(args, "-t") + 1]);
        Assert.True(At(args, "-t") < At(args, "-i"));
    }

    [Fact]
    public void The_whole_clip_reads_from_the_start_as_before()
    {
        Assert.DoesNotContain("-ss", FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", 1));
        Assert.DoesNotContain("-ss", FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_qsv", 1, 60));
    }
}
