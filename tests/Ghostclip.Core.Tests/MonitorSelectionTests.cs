using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Picking the monitor by name, and recording the same one whichever capture method runs.
public class MonitorSelectionTests
{
    private static CaptureSettings Qsv(CaptureMethod capture, int monitor = 1, long handle = 0) =>
        new(EncoderKind.Qsv, EncoderInput.Direct, VideoCodec.H264, monitor, Fps: 60, BitrateMbps: 20, OutputHeight: 0, QsvAsyncDepth: 1,
            Capture: capture, MonitorHandle: handle);

    // Graphics Capture numbers monitors in another order than Desktop Duplication's outputs: the same
    // number could record the other screen. The worker resolves the output to its monitor instead.
    [Fact]
    public void Graphics_capture_records_the_monitor_desktop_duplication_numbers()
    {
        Assert.StartsWith("gfxcapture=hmonitor=65537:max_framerate=60:capture_cursor=1,", FfmpegArgs.Video(Qsv(CaptureMethod.GraphicsCapture, handle: 0x10001)).Filter);
    }

    [Fact]
    public void Without_the_monitor_s_handle_graphics_capture_falls_back_to_its_own_numbering()
    {
        Assert.StartsWith("gfxcapture=monitor_idx=1:", FfmpegArgs.Video(Qsv(CaptureMethod.GraphicsCapture)).Filter);
    }

    [Fact]
    public void Desktop_duplication_keeps_the_output_number()
    {
        Assert.StartsWith("ddagrab=output_idx=1:", FfmpegArgs.Video(Qsv(CaptureMethod.DesktopDuplication, handle: 0x10001)).Filter);
    }

    [Fact]
    public void A_handle_with_the_high_bit_set_reaches_ffmpeg_as_the_same_unsigned_value()
    {
        Assert.Contains("hmonitor=18446744071562072628:", FfmpegArgs.Video(Qsv(CaptureMethod.GraphicsCapture, handle: unchecked((long)0xFFFFFFFF80001234))).Filter);
    }

    [Fact]
    public void Only_graphics_capture_takes_the_handle()
    {
        Assert.Equal(7, CaptureSettings.From(new GhostclipConfig { Capture = CaptureMethod.GraphicsCapture }, EncoderKind.Qsv, EncoderInput.Direct, monitorHandle: 7).MonitorHandle);
        Assert.Equal(0, CaptureSettings.From(new GhostclipConfig(), EncoderKind.Qsv, EncoderInput.Direct, monitorHandle: 7).MonitorHandle);
    }

    private static DisplayOutput Output(int index, string device, bool primary = false, int left = 0) => new(index, device, left, 0, 1920, 1080, primary);

    [Fact]
    public void Each_output_gets_its_monitor_s_name_and_mode()
    {
        var list = MonitorList.Build(
            [Output(0, @"\\.\DISPLAY2", primary: true), Output(1, @"\\.\DISPLAY1", left: -2560)],
            [new(@"\\.\DISPLAY1", "LG ULTRAGEAR", BuiltIn: false, 2560, 1440), new(@"\\.\display2", "DELL U2720Q", BuiltIn: false, 3840, 2160)]);
        Assert.Equal([new MonitorInfo(0, "DELL U2720Q", 0, 0, 3840, 2160, Primary: true, BuiltIn: false), new MonitorInfo(1, "LG ULTRAGEAR", -2560, 0, 2560, 1440, Primary: false, BuiltIn: false)], list);
    }

    [Fact]
    public void A_monitor_windows_has_no_name_for_keeps_the_output_s_size()
    {
        // A laptop's own panel usually has no friendly name; an unmatched output has nothing at all.
        var list = MonitorList.Build([Output(0, @"\\.\DISPLAY1"), Output(1, @"\\.\DISPLAY9")], [new(@"\\.\DISPLAY1", "  ", BuiltIn: true, 0, 0)]);
        Assert.Equal([new MonitorInfo(0, null, 0, 0, 1920, 1080, false, BuiltIn: true), new MonitorInfo(1, null, 0, 0, 1920, 1080, false, BuiltIn: false)], list);
    }

    [Fact]
    public void A_duplicated_screen_takes_the_first_name_it_has()
    {
        // Duplicate mode: one output, two monitors behind it.
        var list = MonitorList.Build([Output(0, @"\\.\DISPLAY1")],
            [new(@"\\.\DISPLAY1", "", BuiltIn: true, 1920, 1080), new(@"\\.\DISPLAY1", "Projector", BuiltIn: false, 1920, 1080)]);
        Assert.Equal("Projector", Assert.Single(list).Name);
    }

    [Fact]
    public void The_settings_page_gets_the_monitors()
    {
        var message = new LibraryEvent("settings", Monitors: [new MonitorInfo(0, "DELL U2720Q", -1920, 0, 2560, 1440, Primary: true, BuiltIn: false)]);
        Assert.Contains("\"monitors\":[{\"index\":0,\"name\":\"DELL U2720Q\",\"left\":-1920,\"top\":0,\"width\":2560,\"height\":1440,\"primary\":true,\"builtIn\":false}]",
            JsonSerializer.Serialize(message, LibraryJson.Default.LibraryEvent));
    }
}
