using Ghostclip.Core;

namespace Ghostclip.Integration.Tests;

// A clip's details for the library (ClipMetaService): its keyframes, where a trim can start, and its waveform,
// read from what the real FFmpeg prints, as the app reads it.
public class DetailsTests
{
    private static async Task<IReadOnlyList<float>> PeaksAsync(string clip, double seconds) =>
        ClipMedia.ParsePeaks((await Ff.ToolAsync("ffmpeg", FfmpegArgs.AudioPeaks(clip, ClipMedia.SamplesPerPeak(seconds)))).StdErr);

    [Fact]
    public async Task The_keyframes_are_read_where_they_are()
    {
        Ff.SkipWithout();
        var r = await Ff.ToolAsync("ffprobe", FfmpegArgs.Keyframes(Ff.Clip));
        Assert.Equal(Ff.ClipKeyframes, ClipMedia.ParseKeyframes(r.StdOut));
    }

    [Fact]
    public async Task The_waveform_has_a_peak_for_each_bucket_at_the_level_of_the_sound()
    {
        Ff.SkipWithout();
        var peaks = await PeaksAsync(Ff.Clip, Ff.ClipSeconds);
        // About one per bucket; the last can be short of samples, or there's one more for what's left.
        Assert.InRange(peaks.Count, ClipMedia.PeakCount - 2, ClipMedia.PeakCount + 2);
        // A steady tone, as FFmpeg's sine makes it, an eighth of full scale.
        Assert.All(peaks.Skip(3).Take(peaks.Count - 6), p => Assert.InRange(p, 0.08f, 0.2f));
    }

    [Fact]
    public async Task The_details_come_together_as_the_app_caches_them()
    {
        Ff.SkipWithout();
        var keyframes = await Ff.ToolAsync("ffprobe", FfmpegArgs.Keyframes(Ff.Clip));
        var peaks = await Ff.ToolAsync("ffmpeg", FfmpegArgs.AudioPeaks(Ff.Clip, ClipMedia.SamplesPerPeak(Ff.ClipSeconds)));
        var meta = ClipMedia.BuildMeta(Ff.ClipSeconds, 108, 30, 2, "h264", keyframes, peaks, ["Game", "Microphone"]);

        Assert.Equal(Ff.ClipKeyframes, meta.Keyframes);
        Assert.InRange(meta.Peaks.Count, ClipMedia.PeakCount - 2, ClipMedia.PeakCount + 2);
        // A clip this short is its own moment.
        Assert.Empty(meta.LoudMoments);
    }

    [Fact]
    public async Task A_part_muted_shows_flat_in_the_waveform()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var muted = Path.Combine(dir, "muted.mp4");
            await Ff.RunAsync(FfmpegArgs.MutePart(Ff.Clip, muted, 2, 4));
            var peaks = await PeaksAsync(muted, Ff.ClipSeconds);
            var perSecond = peaks.Count / Ff.ClipSeconds;
            float At(double seconds) => peaks[(int)(seconds * perSecond)];

            Assert.True(At(3) < 0.01f, $"{At(3)} in the muted part");
            Assert.True(At(1) > 0.08f, $"{At(1)} before it");
            Assert.True(At(5) > 0.08f, $"{At(5)} after it");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
