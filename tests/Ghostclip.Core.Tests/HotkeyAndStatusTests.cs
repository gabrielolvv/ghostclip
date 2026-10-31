using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class HotkeyAndStatusTests
{
    // As the clip hotkey, Ctrl+C stopped copying in every program on one person's PC.
    [Theory]
    [InlineData("Ctrl+C", true)]
    [InlineData("control+v", true)]
    [InlineData(" Ctrl + Z ", true)]
    [InlineData("Shift+Ctrl+Z", true)]
    [InlineData("Alt+F4", true)]
    [InlineData("Alt+Tab", true)]
    [InlineData("Ctrl+Alt+C", false)]
    [InlineData("Ctrl+Shift+C", false)]
    [InlineData("Alt+F10", false)]
    [InlineData("F9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Common_shortcuts_are_recognized_however_written(string? gesture, bool common)
    {
        Assert.Equal(common, HotkeyRules.TakesCommonShortcut(gesture));
    }

    [Fact]
    public void A_common_shortcut_saved_before_goes_back_to_the_default()
    {
        var config = new GhostclipConfig { Hotkey = "Ctrl+C", ScreenshotHotkey = "Ctrl+V", SessionHotkey = "Alt+F4" }.Normalize();
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9"], config.Hotkeys);
        Assert.Equal("Ctrl+Alt+C", new GhostclipConfig { Hotkey = "Ctrl+Alt+C" }.Normalize().Hotkey);
    }

    // The library shows why the recorder stopped; once it records again the reason is gone.
    [Fact]
    public void The_reason_travels_only_while_stopped()
    {
        const string why = "No hardware encoder works on this PC.";
        Assert.Equal(why, RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Stopped }, 60, why).Problem);
        Assert.Null(RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Recording }, 60, why).Problem);
        Assert.Null(RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Probing }, 60, why).Problem);
        // Same state, new reason: a different status, so it gets written.
        Assert.NotEqual(RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Stopped }, 60, why),
            RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Stopped }, 60, "FFmpeg was not found."));
    }

    // The library offers the downloaded update whatever the recorder is doing, and learns of it through a new write.
    [Fact]
    public void A_ready_update_travels_in_any_state()
    {
        var recording = new WorkerStatus { State = WorkerState.Recording };
        Assert.Null(RecorderStatus.From(1, recording, 60).UpdateVersion);
        Assert.Equal("0.7.0", RecorderStatus.From(1, recording, 60, updateVersion: "0.7.0").UpdateVersion);
        Assert.Equal("0.7.0", RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Stopped }, 60, "x", "0.7.0").UpdateVersion);
        Assert.NotEqual(RecorderStatus.From(1, recording, 60), RecorderStatus.From(1, recording, 60, updateVersion: "0.7.0"));
    }
}
