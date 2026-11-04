using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Stops recording and deletes the buffer until asked to go on: nothing on screen reaches the disk meanwhile.
public class PauseRecordingTests
{
    [Fact]
    public void Its_hotkey_takes_no_key_until_one_is_chosen()
    {
        var config = new GhostclipConfig().Normalize();
        Assert.False(config.HasPauseHotkey);
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8"], config.ChosenHotkeys);
    }

    [Fact]
    public void Its_hotkey_comes_last_in_the_hotkey_list()
    {
        // The tray finds each action's key by its position here.
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8", "", ""], new GhostclipConfig().Normalize().Hotkeys);
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8", "", "Alt+F7"], new GhostclipConfig { PauseHotkey = " Alt+F7 " }.Normalize().Hotkeys);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("Ctrl+V")]
    public void A_blank_key_or_a_common_shortcut_leaves_it_off(string? key)
    {
        Assert.Equal("", new GhostclipConfig { PauseHotkey = key! }.Normalize().PauseHotkey);
    }

    [Theory]
    [InlineData("Alt+F10")]
    [InlineData("Alt+F11")]
    [InlineData("Alt+F9")]
    [InlineData("alt + f8")]
    public void On_a_key_another_action_uses_it_goes_back_to_off(string key)
    {
        var config = new GhostclipConfig { PauseHotkey = key }.Normalize();
        Assert.False(config.HasPauseHotkey);
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8"], config.ChosenHotkeys);
    }

    [Fact]
    public void On_the_short_clip_key_it_goes_back_to_off()
    {
        var config = new GhostclipConfig { ShortClipHotkey = "Alt+F7", PauseHotkey = "Alt+F7" }.Normalize();
        Assert.Equal("Alt+F7", config.ShortClipHotkey);
        Assert.False(config.HasPauseHotkey);
    }

    [Fact]
    public void A_moved_hotkey_never_lands_on_it()
    {
        // A hotkey on the clip's key moves to the first free spare: Alt+F12 is this one's here.
        var screenshot = new GhostclipConfig { Hotkey = "Alt+F11", PauseHotkey = "Alt+F12" }.Normalize();
        Assert.Equal("Alt+F12", screenshot.PauseHotkey);
        Assert.Equal("Ctrl+Alt+F11", screenshot.ScreenshotHotkey);

        var session = new GhostclipConfig { Hotkey = "Alt+F9", PauseHotkey = "Alt+F12" }.Normalize();
        Assert.Equal("Alt+F12", session.PauseHotkey);
        Assert.Equal("Ctrl+Alt+F11", session.SessionHotkey);

        var mute = new GhostclipConfig { Hotkey = "Alt+F8", PauseHotkey = "Alt+F12" }.Normalize();
        Assert.Equal("Alt+F12", mute.PauseHotkey);
        Assert.Equal("Ctrl+Alt+F11", mute.MicMuteHotkey);
    }

    [Fact]
    public void A_config_from_before_this_hotkey_leaves_it_off()
    {
        var config = JsonSerializer.Deserialize("""{"hotkey":"Alt+F10"}""", CoreJson.Default.GhostclipConfig)!.Normalize();
        Assert.False(config.HasPauseHotkey);
    }

    [Fact]
    public void Changing_its_hotkey_keeps_recording()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { PauseHotkey = "Alt+F7" }));
    }

    [Fact]
    public void The_worker_is_told_whether_to_record()
    {
        var json = JsonSerializer.Serialize(new IpcMessage { Type = IpcType.Pause, Paused = true }, CoreJson.Default.IpcMessage);
        Assert.Contains("\"type\":\"pause\"", json);
        Assert.True(JsonSerializer.Deserialize(json, CoreJson.Default.IpcMessage)!.Paused);
    }

    [Fact]
    public void The_library_sees_the_state_without_a_frame_rate_or_a_problem()
    {
        var status = RecorderStatus.From(1, new WorkerStatus { State = WorkerState.Paused, Encoder = "h264_nvenc", Fps = 60 }, 60, "stale problem");
        Assert.Equal(WorkerState.Paused, status.State);
        Assert.Equal(0, status.Fps);
        Assert.Null(status.Problem);
        Assert.Contains("\"state\":\"Paused\"", JsonSerializer.Serialize(new WorkerStatus { State = WorkerState.Paused }, CoreJson.Default.WorkerStatus));
    }

    [Fact]
    public void The_switch_starts_on_recording()
    {
        Assert.False(new PauseSwitch().Paused);
    }

    [Fact]
    public void A_change_wakes_whoever_waits_for_one()
    {
        var pause = new PauseSwitch();
        var changed = pause.Changed;
        Assert.False(changed.IsCompleted);
        pause.Set(true);
        Assert.True(changed.IsCompleted);
        Assert.True(pause.Paused);
    }

    [Fact]
    public void Setting_the_same_value_wakes_nobody()
    {
        var pause = new PauseSwitch();
        var changed = pause.Changed;
        pause.Set(false);
        Assert.False(changed.IsCompleted);
    }

    [Fact]
    public void After_a_change_the_next_wait_waits_for_the_next_change()
    {
        var pause = new PauseSwitch();
        pause.Set(true);
        var changed = pause.Changed;
        Assert.False(changed.IsCompleted);
        pause.Set(false);
        Assert.True(changed.IsCompleted);
        Assert.False(pause.Paused);
    }
}
