using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The copies for Discord and for Shorts can fade in and out, picture and sound, so they don't start and end dry.
public class CopyFadeTests
{
    private static readonly CompressionPlan Plan = new(8000, 128, 720, 30);

    private static List<List<string>> Both(int total, double fadeFor, IReadOnlyList<int>? keep = null, IReadOnlyList<double>? volumes = null, IReadOnlyList<int>? denoise = null) =>
    [
        [.. FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", total, keep, volumes, denoise, fadeFor)],
        [.. FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", total, 60, keep, volumes, denoise: denoise, fadeFor: fadeFor)],
    ];

    private static string Video(List<string> args) => args[args.IndexOf("-vf") + 1];

    private static string Graph(List<string> args) => args[args.IndexOf("-filter_complex") + 1];

    private const string VideoFade = "fade=t=in:st=0:d=0.5,fade=t=out:st=29.5:d=0.5";
    private const string AudioFade = "afade=t=in:st=0:d=0.5,afade=t=out:st=29.5:d=0.5";

    [Fact]
    public void The_picture_fades_before_it_becomes_what_the_encoder_takes()
    {
        Assert.All(Both(1, 30), args => Assert.EndsWith($",{VideoFade},format=nv12", Video(args)));
    }

    [Fact]
    public void One_track_as_recorded_goes_through_the_fade()
    {
        Assert.All(Both(1, 30), args =>
        {
            Assert.Equal($"[0:a:0]{AudioFade}[a]", Graph(args));
            Assert.Contains("[a]", args);
            Assert.DoesNotContain("0:a:0?", args);
        });
    }

    [Fact]
    public void One_track_with_its_own_filter_fades_after_it()
    {
        Assert.All(Both(2, 30, keep: [1], volumes: [1, 0.5], denoise: [1]), args =>
            Assert.Equal($"[0:a:1]afftdn=nf=-30,volume=0.5,{AudioFade}[a]", Graph(args)));
    }

    [Fact]
    public void Several_tracks_fade_once_mixed()
    {
        Assert.All(Both(2, 30), args =>
            Assert.Equal($"[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[m];[m]{AudioFade}[a]", Graph(args)));
    }

    [Fact]
    public void A_silent_copy_fades_only_the_picture()
    {
        Assert.All(Both(2, 30, keep: []), args =>
        {
            Assert.Contains("-an", args);
            Assert.DoesNotContain("-filter_complex", args);
            Assert.Contains("fade=t=in", Video(args));
        });
    }

    [Fact]
    public void Without_it_nothing_changes()
    {
        Assert.All(Both(1, 0), args =>
        {
            Assert.DoesNotContain("fade", Video(args));
            Assert.DoesNotContain("-filter_complex", args);
            Assert.Contains("0:a:0?", args);
        });
        Assert.All(Both(2, 0), args =>
            Assert.Equal("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args)));
    }

    [Theory]
    [InlineData(1, "0.25", "0.75")]    // Half a second each way would leave little: a quarter of the clip.
    [InlineData(2, "0.5", "1.5")]
    [InlineData(0.4, "0.1", "0.3")]
    public void A_short_clip_fades_for_less(double duration, string fade, string outAt)
    {
        var video = Video(Both(1, duration)[0]);
        Assert.Contains($"fade=t=in:st=0:d={fade}", video);
        Assert.Contains($"fade=t=out:st={outAt}:d={fade}", video);
    }

    [Fact]
    public void The_times_use_a_point_whatever_the_language_of_Windows()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            Assert.Contains("st=29.5:d=0.5", Video(Both(1, 30)[0]));
            Assert.Contains("st=29.5:d=0.5", Graph(Both(1, 30)[0]));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Request_carries_it_from_the_page_and_is_off_without_it()
    {
        Assert.True(JsonSerializer.Deserialize("""{"type":"vertical","path":"a.mp4","fade":true}""", LibraryJson.Default.LibraryRequest)!.Fade);
        Assert.False(JsonSerializer.Deserialize("""{"type":"vertical","path":"a.mp4"}""", LibraryJson.Default.LibraryRequest)!.Fade);
    }

    [Fact]
    public void Ffmpeg_build_has_the_filters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        var filters = flags.Single(l => l.StartsWith("--enable-filter=", StringComparison.Ordinal)).Split('=')[1].Split(',');
        var verify = File.ReadAllText(Path.Combine(dir.FullName, "build", "ffmpeg", "verify.ps1"));
        foreach (var filter in new[] { "fade", "afade" })
        {
            Assert.Contains(filter, filters);
            Assert.Contains($"'{filter}'", verify);
        }
    }
}
