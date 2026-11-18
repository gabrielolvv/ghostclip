using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Cutting the marked part out of a clip and keeping the rest, by stream copy.
public class CutOutTests
{
    // A minute with a keyframe every 2 s, as Ghostclip records.
    private static readonly double[] Keys = [.. Enumerable.Range(0, 30).Select(i => i * 2.0)];

    [Fact]
    public void Keeps_what_comes_before_and_after_from_the_keyframe_at_or_before_the_end()
    {
        Assert.Equal([new CutOut.Part(0, 10), new CutOut.Part(20, null)], CutOut.Kept(10, 20.5, 60, Keys));
    }

    [Fact]
    public void An_end_on_a_keyframe_goes_on_from_it()
    {
        Assert.Equal([new CutOut.Part(0, 10), new CutOut.Part(12, null)], CutOut.Kept(10, 12, 60, Keys));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.02)]
    [InlineData(-3)]
    public void From_the_start_keeps_only_what_comes_after(double start)
    {
        Assert.Equal([new CutOut.Part(20, null)], CutOut.Kept(start, 20.5, 60, Keys));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(59.99)]
    [InlineData(70)]
    public void To_the_end_keeps_only_what_comes_before(double end)
    {
        Assert.Equal([new CutOut.Part(0, 10)], CutOut.Kept(10, end, 60, Keys));
    }

    [Theory]
    [InlineData(10, 11)]
    [InlineData(10.5, 11.5)]
    [InlineData(0, 60)]
    [InlineData(20, 10)]
    [InlineData(10, 10)]
    public void Nothing_to_cut_out_keeps_nothing(double start, double end)
    {
        // Within one keyframe's stretch nothing would go without re-encoding; all of it would leave nothing.
        Assert.Empty(CutOut.Kept(start, end, 60, Keys));
    }

    [Fact]
    public void The_list_takes_the_clip_twice_with_its_points()
    {
        var list = FfmpegArgs.CutOutList(@"C:\Videos\Ghostclip\G\it's.mp4", [new CutOut.Part(0, 10.5), new CutOut.Part(20, null)]);
        Assert.Equal(
            "ffconcat version 1.0\n" +
            "file 'C:/Videos/Ghostclip/G/it'\\''s.mp4'\n" +
            "outpoint 10.5\n" +
            "file 'C:/Videos/Ghostclip/G/it'\\''s.mp4'\n" +
            "inpoint 20.000000\n",
            list);
    }

    [Fact]
    public void A_keyframe_time_keeps_its_microseconds()
    {
        // A keyframe at 60 fps: rounded to 21.667 it would be past it, to 21.666 before it, and the demuxer
        // would seek back to the keyframe before.
        var list = FfmpegArgs.CutOutList("a.mp4", [new CutOut.Part(21.666667, null)]);
        Assert.Contains("inpoint 21.666667\n", list);
    }

    [Theory]
    [InlineData(1, 60, 1 / 60.0)]
    [InlineData(2, 30, 2 / 30.0)]
    [InlineData(0, 60, 0)]
    [InlineData(1, 0, 0)]
    public void B_frames_put_a_keyframe_ahead_in_decoding_by_as_many_frames(int bFrames, double fps, double lead) =>
        Assert.Equal(lead, CutOut.OutLead(bFrames, fps), 9);

    [Fact]
    public void With_b_frames_the_out_point_comes_that_much_earlier()
    {
        // The demuxer stops by decoding time, where a keyframe at 10 s comes a frame early: at 10 it would come in,
        // a frame of the part cut out.
        var list = FfmpegArgs.CutOutList("a.mp4", [new CutOut.Part(0, 10), new CutOut.Part(20, null)], CutOut.OutLead(1, 60));
        Assert.Contains("outpoint 9.983333\n", list);
        Assert.Contains("inpoint 20.000000\n", list);
    }

    [Fact]
    public void A_cut_copy_is_named_and_listed_as_a_copy_of_its_clip()
    {
        const string name = "Elden Ring_2026-11-16_10-00-00_cut.mp4";
        Assert.True(ClipLibrary.IsDerived(name));
        Assert.True(ClipLibrary.IsGhostclipName("Elden Ring", Path.GetFileNameWithoutExtension(name)));
        Assert.Equal(new DateTime(2026, 11, 16, 10, 0, 0), ClipLibrary.TimeFromName(name));
        Assert.True(ClipLibrary.IsDerived("Elden Ring_2026-11-16_10-00-00_trim_cut_2.mp4"));
        var source = Path.Combine("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4");
        Assert.Equal(source, ClipLibrary.SourceOf(Path.Combine("Elden Ring", name), p => p == source));
    }
}
