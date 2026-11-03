using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Windows Graphics Capture of the game's window alone, and what both Graphics Capture modes need from the worker.
public class GameWindowCaptureTests
{
    private const long Game = 100, Browser = 200, NewGameWindow = 300;

    private static CaptureSettings Qsv(CaptureMethod capture, long window = 0, int canvasWidth = 0, int canvasHeight = 0) =>
        new(EncoderKind.Qsv, EncoderInput.Direct, VideoCodec.H264, Monitor: 0, Fps: 60, BitrateMbps: 20, OutputHeight: 0, QsvAsyncDepth: 1,
            Capture: capture, Window: window, CanvasWidth: canvasWidth, CanvasHeight: canvasHeight);

    // The window is fitted into its monitor's size: window and monitor segments then match and join, and a
    // resized window still shows whole.
    [Fact]
    public void A_game_window_is_fitted_into_its_monitor_s_size()
    {
        var (_, filter, _) = FfmpegArgs.Video(Qsv(CaptureMethod.GameWindow, window: 0x1A2B, canvasWidth: 1920, canvasHeight: 1080));
        Assert.Equal("gfxcapture=hwnd=6699:max_framerate=60:capture_cursor=1:width=1920:height=1080:resize_mode=scale_aspect,hwmap=derive_device=qsv,format=qsv", filter);
    }

    [Fact]
    public void An_odd_canvas_is_rounded_down_to_even_and_an_unknown_one_follows_the_window()
    {
        Assert.Contains(":width=1366:height=766:", FfmpegArgs.Video(Qsv(CaptureMethod.GameWindow, window: 7, canvasWidth: 1367, canvasHeight: 767)).Filter);
        Assert.Contains(":width=-2:height=-2:", FfmpegArgs.Video(Qsv(CaptureMethod.GameWindow, window: 7)).Filter);
    }

    [Fact]
    public void A_window_handle_with_the_high_bit_set_is_written_as_windows_sees_it()
    {
        // HWNDs are 32-bit values; a sign-extended one must not reach FFmpeg as a negative number.
        Assert.Contains("hwnd=2147488308:", FfmpegArgs.Video(Qsv(CaptureMethod.GameWindow, window: unchecked((long)0xFFFFFFFF80001234))).Filter);
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

    // Graphics Capture only hands over a frame when the picture changes. A still screen isn't a hung recorder, but
    // waiting long would have FFmpeg's constant rate repeat minutes of frames in one burst when the picture moves again.
    [Fact]
    public void A_still_screen_restarts_graphics_capture_after_half_a_minute()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), CaptureStall.LimitFor(CaptureMethod.DesktopDuplication));
        Assert.Equal(TimeSpan.FromSeconds(30), CaptureStall.LimitFor(CaptureMethod.GraphicsCapture));
        Assert.Equal(TimeSpan.FromSeconds(30), CaptureStall.LimitFor(CaptureMethod.GameWindow));
    }

    [Fact]
    public void Switching_to_it_restarts_recording()
    {
        Assert.False(new GhostclipConfig { Capture = CaptureMethod.GraphicsCapture }.SameCaptureAs(new GhostclipConfig { Capture = CaptureMethod.GameWindow }));
    }

    private static Func<long, GameWindowTarget.WindowState> Windows(long[] alive, long[]? minimized = null) =>
        w => new(alive.Contains(w), minimized?.Contains(w) == true);

    [Fact]
    public void With_no_game_the_monitor_is_recorded()
    {
        Assert.Equal((0L, 0L), GameWindowTarget.Choose(preferred: 0, reported: 0, Windows([])));
    }

    [Fact]
    public void A_game_that_comes_to_the_front_is_followed()
    {
        Assert.Equal((Game, Game), GameWindowTarget.Choose(preferred: 0, reported: Game, Windows([Game])));
    }

    [Fact]
    public void Alt_tabbing_to_another_fullscreen_program_stays_on_the_game()
    {
        // A browser in F11 counts as a game for naming clips, but the game is still open: recording stays on it.
        Assert.Equal((Game, Game), GameWindowTarget.Choose(preferred: Game, reported: Browser, Windows([Game, Browser])));
    }

    [Fact]
    public void A_minimized_game_records_the_monitor_until_it_is_back()
    {
        Assert.Equal((0L, Game), GameWindowTarget.Choose(preferred: Game, reported: Browser, Windows([Game, Browser], minimized: [Game])));
        Assert.Equal((Game, Game), GameWindowTarget.Choose(preferred: Game, reported: Game, Windows([Game])));
    }

    [Fact]
    public void A_closed_window_hands_over_to_what_is_in_front_now()
    {
        // The launcher closed and the game opened in a new window, or the game quit to the desktop.
        Assert.Equal((NewGameWindow, NewGameWindow), GameWindowTarget.Choose(preferred: Game, reported: NewGameWindow, Windows([NewGameWindow])));
        Assert.Equal((0L, 0L), GameWindowTarget.Choose(preferred: Game, reported: 0, Windows([])));
    }
}
