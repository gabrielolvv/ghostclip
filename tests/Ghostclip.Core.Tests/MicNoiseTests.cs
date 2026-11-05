using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// In the copies for Discord and for Shorts, a track can go through noise reduction. The original stays as it is.
public class MicNoiseTests
{
    private static readonly CompressionPlan Plan = new(8000, 128, 0, 0);

    private static List<List<string>> Both(int total, IReadOnlyList<int>? keep, IReadOnlyList<double>? volumes, IReadOnlyList<int>? denoise) =>
    [
        [.. FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", total, keep, volumes, denoise)],
        [.. FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", total, 60, keep, volumes, denoise: denoise)],
    ];

    private static string Graph(List<string> args) => args[args.IndexOf("-filter_complex") + 1];

    [Fact]
    public void The_track_goes_through_the_filter_before_the_mix()
    {
        Assert.All(Both(2, null, null, [1]), args =>
            Assert.Equal("[0:a:1]afftdn=nf=-30[v1];[0:a:0][v1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void Noise_comes_out_before_the_volume_changes()
    {
        Assert.All(Both(2, null, [1, 0.5], [1]), args =>
            Assert.Equal("[0:a:1]afftdn=nf=-30,volume=0.5[v1];[0:a:0][v1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void Other_tracks_keep_their_volume()
    {
        Assert.All(Both(2, null, [1.5, 1], [1]), args =>
            Assert.Equal("[0:a:0]volume=1.5[v0];[0:a:1]afftdn=nf=-30[v1];[v0][v1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void One_track_goes_through_it_alone()
    {
        Assert.All(Both(2, [1], null, [1]), args =>
        {
            Assert.Equal("[0:a:1]afftdn=nf=-30[a]", Graph(args));
            Assert.Contains("[a]", args);
        });
    }

    [Fact]
    public void A_track_left_out_doesnt_matter()
    {
        Assert.All(Both(2, [0], null, [1]), args =>
        {
            Assert.Contains("0:a:0?", args);
            Assert.DoesNotContain("-filter_complex", args);
        });
    }

    [Fact]
    public void A_silent_copy_doesnt_matter()
    {
        Assert.All(Both(2, [], null, [1]), args =>
        {
            Assert.Contains("-an", args);
            Assert.DoesNotContain("-filter_complex", args);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new int[0])]
    public void Without_it_nothing_changes(int[]? denoise)
    {
        Assert.All(Both(2, null, null, denoise), args =>
            Assert.Equal("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void Request_carries_it_from_the_page()
    {
        var request = JsonSerializer.Deserialize("""{"type":"vertical","path":"a.mp4","denoise":[1]}""", LibraryJson.Default.LibraryRequest);
        Assert.Equal([1], request!.Denoise);
    }

    [Fact]
    public void Ffmpeg_build_has_the_filter()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        var filters = flags.Single(l => l.StartsWith("--enable-filter=", StringComparison.Ordinal)).Split('=')[1].Split(',');
        Assert.Contains("afftdn", filters);
        Assert.Contains("'afftdn'", File.ReadAllText(Path.Combine(dir.FullName, "build", "ffmpeg", "verify.ps1")));
    }
}
