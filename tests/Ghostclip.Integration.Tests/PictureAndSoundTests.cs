using Ghostclip.Core;

namespace Ghostclip.Integration.Tests;

// The exports that run on the CPU, and the pictures, by the real FFmpeg on a made-up clip.
public class PictureAndSoundTests
{
    private static async Task<byte[]> HeadAsync(string file, int count)
    {
        var bytes = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        return bytes[..Math.Min(count, bytes.Length)];
    }

    [Fact]
    public async Task A_gif_of_the_marks_is_a_gif()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var output = Path.Combine(dir, "clip.gif");
            await Ff.RunAsync(FfmpegArgs.Gif(Ff.Clip, output, 1, 2));
            Assert.Equal("GIF8"u8.ToArray(), await HeadAsync(output, 4));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_thumbnail_and_a_saved_frame_are_jpegs()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var thumb = Path.Combine(dir, "thumb.jpg");
            await Ff.RunAsync(FfmpegArgs.Thumbnail(Ff.Clip, thumb, 2));
            var frame = Path.Combine(dir, "frame.jpg");
            await Ff.RunAsync(FfmpegArgs.Frame(Ff.Clip, frame, 2.5));
            foreach (var file in new[] { thumb, frame }) Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, await HeadAsync(file, 3));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Audio_only_of_one_track_is_copied_and_of_two_is_mixed()
    {
        Ff.SkipWithout();
        var dir = Ff.Temp();
        try
        {
            var one = Path.Combine(dir, "one.m4a");
            await Ff.RunAsync(FfmpegArgs.AudioOnly(Ff.Clip, one, 1, 3, audioTracks: 2, keep: [1]));
            Assert.Equal(["audio"], await Ff.StreamsAsync(one));
            Assert.InRange(await Ff.DurationAsync(one), 1.9, 2.1);

            var both = Path.Combine(dir, "both.m4a");
            await Ff.RunAsync(FfmpegArgs.AudioOnly(Ff.Clip, both, 1, 3, audioTracks: 2, level: true));
            Assert.Equal(["audio"], await Ff.StreamsAsync(both));
            Assert.True(await Ff.PeakAsync(both, 0, 0.2, 1.8) > -40);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
