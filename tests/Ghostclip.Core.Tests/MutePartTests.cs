using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A copy with the sound of the marked part taken out, to keep something said there from being shared.
public class MutePartTests
{
    private static string Arg(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void Silences_every_track_between_the_marks_and_copies_the_picture()
    {
        var args = FfmpegArgs.MutePart("in.mp4", "out.mp4", 10, 20.5);
        Assert.Equal("in.mp4", Arg(args, "-i"));
        Assert.Equal("0", Arg(args, "-map"));
        Assert.Equal("copy", Arg(args, "-c:v"));
        Assert.Equal("aac", Arg(args, "-c:a"));
        Assert.Equal("volume=enable='between(t,10,20.5)':volume=0", Arg(args, "-filter:a"));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void A_mark_before_the_start_counts_from_it()
    {
        var args = FfmpegArgs.MutePart("in.mp4", "out.mp4", -2, 3.25);
        Assert.Equal("volume=enable='between(t,0,3.25)':volume=0", Arg(args, "-filter:a"));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(8, 3)]
    [InlineData(-4, -1)]
    public void Marks_that_take_in_nothing_are_refused(double start, double end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegArgs.MutePart("in.mp4", "out.mp4", start, end));

    [Fact]
    public void A_muted_copy_is_named_and_listed_as_a_copy_of_its_clip()
    {
        const string name = "Elden Ring_2026-11-16_10-00-00_muted.mp4";
        Assert.True(ClipLibrary.IsDerived(name));
        Assert.True(ClipLibrary.IsGhostclipName("Elden Ring", Path.GetFileNameWithoutExtension(name)));
        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0), ClipLibrary.TimeFromName(name));
        Assert.True(ClipLibrary.IsDerived("Elden Ring_2026-11-16_10-00-00_trim_muted_2.mp4"));
        var source = Path.Combine("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4");
        Assert.Equal(source, ClipLibrary.SourceOf(Path.Combine("Elden Ring", name), p => p == source));
    }
}
