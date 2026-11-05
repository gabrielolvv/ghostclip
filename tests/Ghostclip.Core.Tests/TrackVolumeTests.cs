using System.Text.Json;
using Ghostclip.Core;
using Xunit;

namespace Ghostclip.Core.Tests;

// In the copies for Discord and for Shorts, each track goes in at the volume the person picked.
public class TrackVolumeTests
{
    private static readonly CompressionPlan Plan = new(8000, 128, 0, 0);

    private static List<List<string>> Both(int total, IReadOnlyList<int>? keep, IReadOnlyList<double>? volumes) =>
    [
        [.. FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", total, keep, volumes)],
        [.. FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", total, 60, keep, volumes)],
    ];

    private static string Graph(List<string> args) => args[args.IndexOf("-filter_complex") + 1];

    [Fact]
    public void Each_track_goes_into_the_mix_at_its_own_volume()
    {
        Assert.All(Both(2, null, [1.5, 0.5]), args =>
            Assert.Equal("[0:a:0]volume=1.5[v0];[0:a:1]volume=0.5[v1];[v0][v1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void A_track_at_full_volume_gets_no_filter()
    {
        Assert.All(Both(2, null, [1, 0.25]), args =>
            Assert.Equal("[0:a:1]volume=0.25[v1];[0:a:0][v1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void All_at_full_volume_is_the_mix_as_it_was()
    {
        Assert.All(Both(2, null, [1, 1]), args =>
            Assert.Equal("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void Volumes_go_by_track_number_when_a_track_is_left_out()
    {
        Assert.All(Both(3, [0, 2], [1, 0.1, 0.5]), args =>
            Assert.Equal("[0:a:2]volume=0.5[v2];[0:a:0][v2]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void One_track_at_another_volume_goes_through_the_filter()
    {
        Assert.All(Both(2, [1], [1, 0.5]), args =>
        {
            Assert.Equal("[0:a:1]volume=0.5[a]", Graph(args));
            Assert.Contains("[a]", args);
            Assert.DoesNotContain("0:a:1?", args);
        });
    }

    [Fact]
    public void One_track_at_full_volume_stays_as_it_was()
    {
        Assert.All(Both(2, [1], [0.5, 1]), args =>
        {
            Assert.Contains("0:a:1?", args);
            Assert.DoesNotContain("-filter_complex", args);
        });
    }

    [Fact]
    public void A_silent_copy_ignores_the_volumes()
    {
        Assert.All(Both(2, [], [0.5, 0.5]), args =>
        {
            Assert.Contains("-an", args);
            Assert.DoesNotContain("-filter_complex", args);
        });
    }

    [Fact]
    public void Without_volumes_nothing_changes()
    {
        Assert.All(Both(2, null, null), args =>
            Assert.Equal("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void Volumes_stay_in_range_and_a_missing_one_is_full()
    {
        Assert.All(Both(3, null, [9, -1]), args =>
            Assert.Equal("[0:a:0]volume=2[v0];[0:a:1]volume=0[v1];[v0][v1][0:a:2]amix=inputs=3:duration=first:normalize=0[a]", Graph(args)));
    }

    [Fact]
    public void Volumes_use_a_point_whatever_the_language_of_Windows()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            Assert.Contains("volume=0.75", Graph(Both(2, null, [0.75, 1])[0]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Request_carries_the_volumes_from_the_page()
    {
        var request = JsonSerializer.Deserialize("""{"type":"vertical","path":"C:\\c\\a.mp4","tracks":[0],"volumes":[0.5,1.5]}""", LibraryJson.Default.LibraryRequest);
        Assert.Equal([0.5, 1.5], request!.Volumes);
    }

    [Fact]
    public void Ffmpeg_build_has_the_volume_filter()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        var filters = flags.Single(l => l.StartsWith("--enable-filter=", StringComparison.Ordinal)).Split('=')[1].Split(',');
        Assert.Contains("volume", filters);
        Assert.Contains("'volume'", File.ReadAllText(Path.Combine(dir.FullName, "build", "ffmpeg", "verify.ps1")));
    }
}
