using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// "Even volume" in the copies: the mixed sound goes through loudnorm, so a quiet game and a loud microphone
// come out at one steady level. loudnorm works at 192 kHz, so it's brought back to 48 kHz after.
public class EvenLoudnessTests
{
    private const string Level = "loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000";
    private static readonly CompressionPlan Plan = new(6000, 128, 1080, 60);

    private static string Graph(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-filter_complex") + 1];

    [Fact]
    public void One_plain_track_goes_through_it()
    {
        var args = FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", 1, level: true);
        Assert.Equal($"[0:a:0]{Level}[a]", Graph(args));
        Assert.Contains("[a]", args);
    }

    [Fact]
    public void Several_tracks_are_mixed_first_and_faded_after()
    {
        var args = FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", 2, fadeFor: 10, level: true);
        Assert.Equal($"[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[m];[m]{Level},afade=t=in:st=0:d=0.5,afade=t=out:st=9.5:d=0.5[a]", Graph(args));
    }

    [Fact]
    public void The_vertical_copy_takes_it_too()
    {
        var args = FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_qsv", 1, 60, level: true);
        Assert.Equal($"[0:a:0]{Level}[a]", Graph(args));
    }

    [Fact]
    public void Audio_only_is_encoded_rather_than_copied()
    {
        var args = FfmpegArgs.AudioOnly("in.mp4", "out.m4a", 0, 5, 1, level: true);
        Assert.Equal($"[0:a:0]{Level}[a]", Graph(args));
        Assert.DoesNotContain("copy", args);
    }

    [Fact]
    public void Slow_motion_evens_it_before_slowing_it()
    {
        var args = FfmpegArgs.SlowMotion("in.mp4", "out.mp4", "h264_amf", 0, 4, 0.5, 1, level: true);
        Assert.Equal($"[0:a:0]{Level},atempo=0.5[a]", Graph(args));
    }

    [Fact]
    public void A_silent_copy_has_nothing_to_even()
    {
        var args = FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", 2, keep: [], level: true);
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-filter_complex", args);
    }

    [Fact]
    public void Without_it_nothing_changes()
    {
        Assert.DoesNotContain("-filter_complex", FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", 1));
        Assert.Contains("copy", FfmpegArgs.AudioOnly("in.mp4", "out.m4a", 0, 5, 1));
    }

    [Fact]
    public void The_minimal_ffmpeg_has_loudnorm_and_checks_it()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        var filters = flags.Single(l => l.StartsWith("--enable-filter=", StringComparison.Ordinal)).Split('=')[1].Split(',');
        Assert.Contains("loudnorm", filters);
        Assert.Contains("'loudnorm'", File.ReadAllText(Path.Combine(dir.FullName, "build", "ffmpeg", "verify.ps1")));
    }
}
