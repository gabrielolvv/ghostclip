using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class ProfileTests
{
    private static GhostclipConfig WithProfiles(params GameProfile[] profiles) =>
        new GhostclipConfig { ClipSeconds = 60, Profiles = new GameProfileList(profiles) }.Normalize();

    [Fact]
    public void Profile_overrides_and_inherits()
    {
        var config = WithProfiles(new GameProfile { Game = "Elden Ring", ClipSeconds = 120, AudioSource = AudioSourceMode.GameOnly });
        Assert.Equal(120, config.ClipSecondsFor("elden ring"));
        Assert.Equal(AudioSourceMode.GameOnly, config.AudioSourceFor("ELDEN RING"));
        Assert.False(config.MicrophoneMutedFor("Elden Ring"));
        Assert.Equal(60, config.ClipSecondsFor("Counter-Strike 2"));
        Assert.Equal(AudioSourceMode.System, config.AudioSourceFor(null));
    }

    [Fact]
    public void Buffer_covers_the_longest_profile()
    {
        Assert.Equal(180, WithProfiles(new GameProfile { Game = "A", ClipSeconds = 180 }, new GameProfile { Game = "B", ClipSeconds = 30 }).MaxClipSeconds);
        Assert.Equal(60, WithProfiles().MaxClipSeconds);
    }

    [Fact]
    public void Normalize_drops_nameless_profiles_and_clamps()
    {
        var config = WithProfiles(new GameProfile { Game = "  " }, new GameProfile { Game = " X ", ClipSeconds = 5000 });
        var p = Assert.Single(config.Profiles);
        Assert.Equal("X", p.Game);
        Assert.Equal(600, p.ClipSeconds);
    }

    [Fact]
    public void Profiles_compare_by_content_and_survive_json()
    {
        var dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;
        try
        {
            var path = Path.Combine(dir, "config.json");
            var config = WithProfiles(new GameProfile { Game = "Elden Ring", MuteMicrophone = true });
            ConfigStore.Save(path, config);
            Assert.Equal(config, ConfigStore.Load(path, out _));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Switching_audio_source_or_profiles_keeps_the_buffer_but_mic_does_not()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { AudioSource = AudioSourceMode.GameOnly, Profiles = [new GameProfile { Game = "X" }] }));
        Assert.False(a.SameCaptureAs(a with { Microphone = true }));
    }
}

public class Phase3ArgsTests
{
    private static readonly CaptureSettings Qsv =
        new(EncoderKind.Qsv, EncoderInput.Direct, VideoCodec.H264, 0, 60, 20, 0, 1);

    [Fact]
    public void Microphone_is_a_second_audio_track()
    {
        var args = FfmpegArgs.Record(Qsv, @"C:\b", 0, [@"\\.\pipe\game", @"\\.\pipe\mic"], 0).ToList();
        Assert.Equal(2, args.Count(a => a == "f32le"));
        Assert.Contains("0:a", args);
        Assert.Contains("1:a", args);
        Assert.Single(args, a => a == "-c:a");
    }

    [Fact]
    public void Remux_names_the_audio_tracks()
    {
        var args = FfmpegArgs.Remux("l.ffconcat", "o.mp4", 0, ["Game", "Microphone"]).ToList();
        // handler_name is what MP4 actually stores (verified with ffprobe).
        Assert.Equal("handler_name=Game", args[args.IndexOf("-metadata:s:a:0") + 1]);
        Assert.Equal("handler_name=Microphone", args[args.IndexOf("-metadata:s:a:1") + 1]);
        Assert.Equal("o.mp4", args[^1]);
    }

    [Fact]
    public void Trim_is_stream_copy_between_two_points()
    {
        var args = FfmpegArgs.Trim("in.mp4", "out.mp4", 12.5, 40).ToList();
        Assert.Equal("12.500", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("40.000", args[args.IndexOf("-to") + 1]);
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"), "-ss before -i seeks the input instead of decoding to it");
        Assert.Equal("copy", args[args.IndexOf("-c") + 1]);
    }

    [Fact]
    public void Compress_mixes_tracks_and_uses_the_hardware_encoder()
    {
        var plan = new CompressionPlan(1200, 128, 480, 30);
        var args = FfmpegArgs.Compress("in.mp4", "out.mp4", plan, "h264_qsv", 2).ToList();
        Assert.Contains("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", args);
        Assert.Equal("h264_qsv", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("1200k", args[args.IndexOf("-b:v") + 1]);
        Assert.Equal("scale=-2:480,fps=30,format=nv12", args[args.IndexOf("-vf") + 1]);
        Assert.DoesNotContain("libx264", args);
    }

    [Fact]
    public void Compress_single_track_maps_it_optionally()
    {
        var args = FfmpegArgs.Compress("in.mp4", "out.mp4", new CompressionPlan(8000, 128, 0, 0), "h264_nvenc", 1).ToList();
        Assert.Contains("0:a:0?", args);
        Assert.DoesNotContain("-filter_complex", args);
        Assert.Equal("format=nv12", args[args.IndexOf("-vf") + 1]);
    }
}

public class DesktopAvailabilityTests
{
    private static ProbeResult Fail(EncoderKind e, string error) => new(e, EncoderInput.Direct, false, 1, error);

    [Fact]
    public void Locked_screen_is_not_a_missing_encoder()
    {
        const string denied = "[Parsed_ddagrab_0 @ 000001ee5c5abac0] Desktop duplication access denied";
        Assert.True(EncoderProbe.DesktopUnavailable([Fail(EncoderKind.Nvenc, denied), Fail(EncoderKind.Qsv, denied),
            Fail(EncoderKind.Amf, "h264_amf is not in this FFmpeg build")]));
    }

    [Fact]
    public void A_real_encoder_failure_is_still_a_failure()
    {
        Assert.False(EncoderProbe.DesktopUnavailable([Fail(EncoderKind.Nvenc, "Cannot load nvcuda.dll"),
            Fail(EncoderKind.Qsv, "Desktop duplication access denied")]));
        Assert.False(EncoderProbe.DesktopUnavailable([]));
    }
}

public class CompressionPlannerTests
{
    [Fact]
    public void Sixty_seconds_into_ten_megabytes_drops_to_480p30()
    {
        // 10 MB * 0.92 over 60 s ≈ 1256 kbps total, minus 128 for audio.
        var plan = CompressionPlanner.Plan(60, 1080, 60, 10)!;
        Assert.InRange(plan.VideoKbps, 1100, 1150);
        Assert.Equal(480, plan.Height);
        Assert.Equal(30, plan.Fps);
    }

    [Fact]
    public void Short_clip_keeps_resolution_and_frame_rate()
    {
        var plan = CompressionPlanner.Plan(10, 1080, 60, 10)!;
        Assert.Equal(0, plan.Height);
        Assert.Equal(0, plan.Fps);
    }

    [Fact]
    public void Never_scales_up()
    {
        var plan = CompressionPlanner.Plan(30, 720, 60, 10)!; // ~2.3 Mbps: would pick 480
        Assert.Equal(480, plan.Height);
        Assert.Equal(0, CompressionPlanner.Plan(30, 480, 30, 25)!.Height);
    }

    [Fact]
    public void Too_long_for_the_limit_says_so()
    {
        Assert.Null(CompressionPlanner.Plan(600, 1080, 60, 10));
        Assert.Null(CompressionPlanner.Plan(0, 1080, 60, 10));
    }

    [Fact]
    public void Retry_aims_lower_in_proportion_to_the_overshoot()
    {
        var safety = CompressionPlanner.RetrySafety(11 * 1024 * 1024, 10);
        Assert.InRange(safety, 0.78, 0.80);
    }

    [Fact]
    public void Derived_names_sit_next_to_the_clip()
    {
        var taken = new HashSet<string> { @"C:\c\G\G_1_trim.mp4" };
        Assert.Equal(@"C:\c\G\G_1_trim_2.mp4", ClipNaming.DerivedPath(@"C:\c\G\G_1.mp4", "trim", taken.Contains));
        Assert.Equal(@"C:\c\G\G_1_discord.mp4", ClipNaming.DerivedPath(@"C:\c\G\G_1.mp4", "discord", _ => false));
    }
}
