using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Windows Graphics Capture as an alternative to Desktop Duplication: testers lost FPS while recording,
// and Desktop Duplication can take a game out of its fastest fullscreen path.
public class CaptureMethodTests
{
    private static CaptureSettings Qsv(CaptureMethod capture) =>
        new(EncoderKind.Qsv, EncoderInput.Direct, VideoCodec.H264, Monitor: 1, Fps: 60, BitrateMbps: 20, OutputHeight: 0, QsvAsyncDepth: 1,
            Capture: capture);

    [Fact]
    public void Desktop_duplication_stays_the_default()
    {
        Assert.Equal(CaptureMethod.DesktopDuplication, new GhostclipConfig().Capture);
        var fromOldFile = JsonSerializer.Deserialize("""{ "hotkey": "Alt+F10" }""", CoreJson.Default.GhostclipConfig)!;
        Assert.Equal(CaptureMethod.DesktopDuplication, fromOldFile.Capture);
        Assert.Equal(CaptureMethod.DesktopDuplication, CaptureSettings.From(new GhostclipConfig(), EncoderKind.Qsv, EncoderInput.Direct).Capture);
    }

    [Fact]
    public void Graphics_capture_reads_the_monitor_with_gfxcapture_at_the_clip_rate()
    {
        var (_, filter, _) = FfmpegArgs.Video(Qsv(CaptureMethod.GraphicsCapture));
        Assert.Equal("gfxcapture=monitor_idx=1:max_framerate=60:capture_cursor=1,hwmap=derive_device=qsv,format=qsv", filter);
        Assert.DoesNotContain("ddagrab", filter);
    }

    [Fact]
    public void Desktop_duplication_keeps_ddagrab()
    {
        Assert.StartsWith("ddagrab=output_idx=1:framerate=60,", FfmpegArgs.Video(Qsv(CaptureMethod.DesktopDuplication)).Filter);
    }

    [Fact]
    public void The_method_is_part_of_what_the_log_and_the_badge_read()
    {
        Assert.Equal("h264_qsv (direct)", Qsv(CaptureMethod.DesktopDuplication).Describe());
        Assert.Equal("h264_qsv (direct, graphics capture)", Qsv(CaptureMethod.GraphicsCapture).Describe());
        var status = RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Recording, Encoder = Qsv(CaptureMethod.GraphicsCapture).Describe() }, 60);
        Assert.Equal("QSV", status.Encoder);
    }

    [Fact]
    public void Switching_it_restarts_recording_and_probes_again()
    {
        Assert.False(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { Capture = CaptureMethod.GraphicsCapture }));
        var cache = new ProbeCache("id", VideoCodec.H264, 0, DateTime.UtcNow, [], CaptureMethod.DesktopDuplication);
        Assert.True(cache.Matches("id", VideoCodec.H264, 0, CaptureMethod.DesktopDuplication));
        Assert.False(cache.Matches("id", VideoCodec.H264, 0, CaptureMethod.GraphicsCapture));
    }

    [Fact]
    public void A_probe_cache_from_before_the_setting_counts_as_desktop_duplication()
    {
        var json = """{ "ffmpegId": "id", "codec": "H264", "monitor": 0, "probedUtc": "2026-10-30T19:45:14Z", "results": [] }""";
        var cache = JsonSerializer.Deserialize(json, CoreJson.Default.ProbeCache)!;
        Assert.True(cache.Matches("id", VideoCodec.H264, 0, CaptureMethod.DesktopDuplication));
    }
}
