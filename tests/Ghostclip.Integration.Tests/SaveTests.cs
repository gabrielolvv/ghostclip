using Ghostclip.Core;

namespace Ghostclip.Integration.Tests;

// The hotkey's save: the replay buffer's MPEG-TS segments joined into an MP4 by stream copy (ClipSaver), with the
// recorder's segment options and the app's own concat list and remux, run by the real FFmpeg.
public class SaveTests
{
    private static async Task<string> SaveAsync(string dir, IEnumerable<string> segments, int audioLeadMs = 0)
    {
        // As ClipSaver does: the segments sit beside the list, which names them alone.
        var snapshot = Path.Combine(dir, "snapshot");
        Directory.CreateDirectory(snapshot);
        var names = new List<string>();
        foreach (var segment in segments)
        {
            var name = Path.GetFileName(segment);
            if (!File.Exists(Path.Combine(snapshot, name))) File.Copy(segment, Path.Combine(snapshot, name));
            names.Add(name);
        }
        var list = Path.Combine(snapshot, "list.ffconcat");
        await File.WriteAllTextAsync(list, FfmpegArgs.ConcatList(names), TestContext.Current.CancellationToken);
        var output = Path.Combine(dir, "clip.mp4");
        await Ff.RunAsync(FfmpegArgs.Remux(list, output, audioLeadMs, ["Game", "Microphone"]));
        return output;
    }

    // The clip has no B-frames, so in file order the picture's times only go up, a frame (1/30 s) at a time. It starts
    // a hair after 0: the sound's AAC priming (1024 samples, 21 ms) comes first, and the remux puts that at 0.
    // Where two recordings meet, the demuxer lines the second up by its start, which is the sound's: the frame before
    // it shows those 21 ms longer, less than a frame, at a worker restart only.
    private static void Rising(IReadOnlyList<double> times, int splices = 0)
    {
        Assert.InRange(times[0], -0.001, 0.03);
        var longer = 0;
        for (var i = 1; i < times.Count; i++)
        {
            var step = times[i] - times[i - 1];
            if (step is >= 0.037 and < 0.06) longer++;
            else Assert.True(step is > 0.03 and < 0.037, $"from packet {i - 1} to {i}: {times[i - 1]} then {times[i]}");
        }
        Assert.True(longer <= splices, $"{longer} frames shown longer, for {splices} places where recordings meet");
    }

    [Fact]
    public async Task The_whole_buffer_saves_as_one_mp4_with_named_tracks()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var segments = await Ff.SegmentsAsync(Path.Combine(dir, "buffer"), 100);
            Assert.Equal(3, segments.Count);
            var clip = await SaveAsync(dir, segments);

            Assert.InRange(await Ff.DurationAsync(clip), 5.9, 6.1);
            var streams = await Ff.StreamInfoAsync(clip);
            Assert.Equal(["video", "audio", "audio"], streams.Select(s => s.Type));
            Assert.Equal(["Game", "Microphone"], streams.Skip(1).Select(s => s.Name));
            var times = await Ff.VideoTimesAsync(clip);
            Assert.Equal((await Ff.VideoTimesAsync(Ff.Clip)).Count, times.Count);
            Rising(times);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task The_last_segments_save_from_zero()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var segments = await Ff.SegmentsAsync(Path.Combine(dir, "buffer"), 100);
            var clip = await SaveAsync(dir, segments.Skip(1));

            Assert.InRange(await Ff.DurationAsync(clip), 3.9, 4.1);
            Assert.Equal(120, (await Ff.VideoTimesAsync(clip)).Count);
            Rising(await Ff.VideoTimesAsync(clip));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_segment_still_being_written_cut_anywhere_still_saves()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var segments = await Ff.SegmentsAsync(Path.Combine(dir, "buffer"), 100);
            // As CopyGrowing finds the newest: written up to some byte, not on a packet's edge.
            var growing = Path.Combine(dir, "growing");
            Directory.CreateDirectory(growing);
            var bytes = await File.ReadAllBytesAsync(segments[^1], TestContext.Current.CancellationToken);
            var cut = Path.Combine(growing, Path.GetFileName(segments[^1]));
            await File.WriteAllBytesAsync(cut, bytes[..(bytes.Length * 3 / 5 + 77)], TestContext.Current.CancellationToken);
            var clip = await SaveAsync(dir, [segments[0], segments[1], cut]);

            Assert.InRange(await Ff.DurationAsync(clip), 4.2, 5.9);
            Assert.Equal(["video", "audio", "audio"], await Ff.StreamsAsync(clip));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Segments_from_before_and_after_a_restart_join_in_order()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            // Two recordings: the second starts its timestamps over, as a restarted worker's does.
            var before = await Ff.SegmentsAsync(Path.Combine(dir, "before"), 100);
            var after = await Ff.SegmentsAsync(Path.Combine(dir, "after"), 200);
            var clip = await SaveAsync(dir, [before[^1], after[0], after[1]]);

            Assert.InRange(await Ff.DurationAsync(clip), 5.9, 6.1);
            var times = await Ff.VideoTimesAsync(clip);
            Assert.Equal(180, times.Count);
            Rising(times, splices: 1);
            Assert.True(times.Max() > 5.5, $"the last packet is at {times.Max()} s");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task An_audio_lead_moves_the_sound_earlier_and_keeps_the_picture()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var segments = await Ff.SegmentsAsync(Path.Combine(dir, "buffer"), 100);
            var plain = await SaveAsync(Path.Combine(dir, "plain"), segments);
            var led = await SaveAsync(Path.Combine(dir, "led"), segments, audioLeadMs: 200);

            Assert.Equal(await Ff.PacketHashesAsync(plain, "v:0"), await Ff.PacketHashesAsync(led, "v:0"));
            var a = await Ff.StreamInfoAsync(plain);
            var b = await Ff.StreamInfoAsync(led);
            Assert.Equal(["video", "audio", "audio"], b.Select(s => s.Type));
            Assert.Equal(["Game", "Microphone"], b.Skip(1).Select(s => s.Name));
            // The sound starts 0.2 s earlier against the picture than without the lead.
            var shift = (b[1].Start - b[0].Start) - (a[1].Start - a[0].Start);
            Assert.InRange(shift, -0.25, -0.15);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
