using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Windows Graphics Capture of the game's window alone, and what both Graphics Capture modes need from the worker.
public class GameWindowCaptureTests
{
    private static CaptureSettings Qsv(CaptureMethod capture, long window = 0) =>
        new(EncoderKind.Qsv, EncoderInput.Direct, VideoCodec.H264, Monitor: 0, Fps: 60, BitrateMbps: 20, OutputHeight: 0, QsvAsyncDepth: 1,
            Capture: capture, Window: window);

    [Fact]
    public void With_a_game_window_it_captures_that_window_at_even_dimensions()
    {
        var (_, filter, _) = FfmpegArgs.Video(Qsv(CaptureMethod.GameWindow, window: 0x1A2B));
        Assert.Equal("gfxcapture=hwnd=6699:max_framerate=60:capture_cursor=1:width=-2:height=-2,hwmap=derive_device=qsv,format=qsv", filter);
    }

    [Fact]
    public void Without_a_game_it_captures_the_monitor()
    {
        var (_, filter, _) = FfmpegArgs.Video(Qsv(CaptureMethod.GameWindow));
        Assert.StartsWith("gfxcapture=monitor_idx=0:max_framerate=60:capture_cursor=1,", filter);
    }

    [Fact]
    public void Only_the_game_window_mode_follows_the_window()
    {
        var config = new GhostclipConfig { Capture = CaptureMethod.GameWindow };
        Assert.Equal(42, CaptureSettings.From(config, EncoderKind.Qsv, EncoderInput.Direct, window: 42).Window);
        Assert.Equal(0, CaptureSettings.From(new GhostclipConfig { Capture = CaptureMethod.GraphicsCapture }, EncoderKind.Qsv, EncoderInput.Direct, window: 42).Window);
        Assert.Equal(0, CaptureSettings.From(new GhostclipConfig(), EncoderKind.Qsv, EncoderInput.Direct, window: 42).Window);
    }

    [Fact]
    public void The_log_says_which_mode_ran()
    {
        Assert.Equal("h264_qsv (direct, game window)", Qsv(CaptureMethod.GameWindow, window: 7).Describe());
        Assert.Equal("h264_qsv (direct, graphics capture)", Qsv(CaptureMethod.GameWindow).Describe());
    }

    // Graphics Capture only hands over a frame when the picture changes: a still screen is not a hung recorder.
    [Fact]
    public void A_still_screen_is_not_taken_for_a_hung_recorder_with_graphics_capture()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), CaptureStall.LimitFor(CaptureMethod.DesktopDuplication));
        Assert.Equal(TimeSpan.FromMinutes(5), CaptureStall.LimitFor(CaptureMethod.GraphicsCapture));
        Assert.Equal(TimeSpan.FromMinutes(5), CaptureStall.LimitFor(CaptureMethod.GameWindow));
    }

    [Fact]
    public void Switching_to_it_restarts_recording()
    {
        Assert.False(new GhostclipConfig { Capture = CaptureMethod.GraphicsCapture }.SameCaptureAs(new GhostclipConfig { Capture = CaptureMethod.GameWindow }));
    }
}
