using Ghostclip.Core;

namespace Ghostclip.Integration.Tests;

// What the library does to a clip without a GPU encoder, run by the real FFmpeg on a made-up clip, and checked by
// the packets that come out: a stream copy keeps them byte for byte.
public class StreamCopyTests
{
    [Fact]
    public async Task A_trim_starts_on_the_keyframe_and_keeps_its_packets()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var output = Path.Combine(dir, "trim.mp4");
            await Ff.RunAsync(FfmpegArgs.Trim(Ff.Clip, output, 2, 4));

            var source = await Ff.PacketHashesAsync(Ff.Clip, "v:0");
            var times = await Ff.VideoTimesAsync(Ff.Clip);
            var trimmed = await Ff.PacketHashesAsync(output, "v:0");
            var first = times.ToList().FindIndex(t => t >= 2 - 0.001);
            Assert.Equal(source[first], trimmed[0]);
            Assert.Equal(source.Skip(first).Take(trimmed.Count), trimmed);
            Assert.InRange(await Ff.DurationAsync(output), 1.9, 2.1);
            Assert.Equal(["video", "audio", "audio"], await Ff.StreamsAsync(output));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Cutting_a_part_out_keeps_every_packet_before_and_after_it_and_none_of_it()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var parts = CutOut.Kept(2, 4, Ff.ClipSeconds, Ff.ClipKeyframes);
            var list = Path.Combine(dir, "cut.ffconcat");
            await File.WriteAllTextAsync(list, FfmpegArgs.CutOutList(Ff.Clip, parts), TestContext.Current.CancellationToken);
            var output = Path.Combine(dir, "cut.mp4");
            await Ff.RunAsync(FfmpegArgs.Join(list, output));

            var source = await Ff.PacketHashesAsync(Ff.Clip, "v:0");
            var times = await Ff.VideoTimesAsync(Ff.Clip);
            var kept = source.Where((_, i) => times[i] < 2 - 0.001 || times[i] >= 4 - 0.001).ToList();
            Assert.Equal(kept, await Ff.PacketHashesAsync(output, "v:0"));
            Assert.InRange(await Ff.DurationAsync(output), 3.9, 4.1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Muting_a_part_silences_every_track_there_and_leaves_the_picture_as_it_was()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var output = Path.Combine(dir, "muted.mp4");
            await Ff.RunAsync(FfmpegArgs.MutePart(Ff.Clip, output, 2, 4));

            Assert.Equal(await Ff.PacketHashesAsync(Ff.Clip, "v:0"), await Ff.PacketHashesAsync(output, "v:0"));
            Assert.Equal(["video", "audio", "audio"], await Ff.StreamsAsync(output));
            foreach (var track in new[] { 0, 1 })
            {
                Assert.True(await Ff.PeakAsync(output, track, 2.2, 3.8) < -80, $"track {track} isn't quiet in the marks");
                Assert.True(await Ff.PeakAsync(output, track, 0.3, 1.7) > -40, $"track {track} went quiet before the marks");
                Assert.True(await Ff.PeakAsync(output, track, 4.3, 5.7) > -40, $"track {track} went quiet after the marks");
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_track_taken_out_for_the_player_is_that_track_alone_as_it_was()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var output = Path.Combine(dir, ClipMedia.TrackName("0123456789abcdef", 1));
            await Ff.RunAsync(FfmpegArgs.ExtractTrack(Ff.Clip, output, 1));

            Assert.Equal(["audio"], await Ff.StreamsAsync(output));
            Assert.Equal(await Ff.PacketHashesAsync(Ff.Clip, "a:1"), await Ff.PacketHashesAsync(output, "a:0"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Joining_clips_puts_their_packets_one_after_the_other()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var second = Path.Combine(dir, "second.mp4");
            File.Copy(Ff.Clip, second);
            var list = Path.Combine(dir, "join.ffconcat");
            await File.WriteAllTextAsync(list, FfmpegArgs.ConcatList([Ff.Clip, second]), TestContext.Current.CancellationToken);
            var output = Path.Combine(dir, "joined.mp4");
            await Ff.RunAsync(FfmpegArgs.Join(list, output));

            var source = await Ff.PacketHashesAsync(Ff.Clip, "v:0");
            Assert.Equal([.. source, .. source], await Ff.PacketHashesAsync(output, "v:0"));
            Assert.InRange(await Ff.DurationAsync(output), 11.9, 12.1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
