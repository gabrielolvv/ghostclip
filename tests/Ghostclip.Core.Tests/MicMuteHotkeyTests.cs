using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A hotkey that mutes the microphone track while recording, without restarting it.
public class MicMuteHotkeyTests
{
    [Fact]
    public void It_is_alt_f8_and_comes_last_in_the_hotkey_list()
    {
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8"], new GhostclipConfig().Normalize().Hotkeys);
    }

    [Fact]
    public void A_blank_or_common_shortcut_goes_back_to_the_default()
    {
        Assert.Equal("Alt+F8", new GhostclipConfig { MicMuteHotkey = " " }.Normalize().MicMuteHotkey);
        Assert.Equal("Alt+F8", new GhostclipConfig { MicMuteHotkey = "Ctrl+V" }.Normalize().MicMuteHotkey);
    }

    [Fact]
    public void A_clip_hotkey_on_alt_f8_moves_the_mute_hotkey_to_a_spare()
    {
        // A clip hotkey picked before this one existed keeps its key.
        var config = new GhostclipConfig { Hotkey = "Alt+F8" }.Normalize();
        Assert.Equal("Alt+F8", config.Hotkey);
        Assert.Equal(4, config.Hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_moved_hotkey_never_lands_on_the_mute_one()
    {
        // The screenshot hotkey on the clip's key moves to a spare: not Alt+F8, which muting holds.
        var config = new GhostclipConfig { ScreenshotHotkey = "Alt+F10", SessionHotkey = "Alt+F11", MicMuteHotkey = "Alt+F12" }.Normalize();
        Assert.Equal(4, config.Hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Alt+F12", config.MicMuteHotkey);
    }

    [Fact]
    public void Changing_it_keeps_recording()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { MicMuteHotkey = "Ctrl+Alt+M" }));
    }

    [Fact]
    public void Muted_by_the_hotkey_or_by_the_game_s_profile()
    {
        var config = new GhostclipConfig { Profiles = new([new GameProfile { Game = "Valorant", MuteMicrophone = true }]) };
        Assert.False(config.MicrophoneMutedFor("Elden Ring", byHotkey: false));
        Assert.True(config.MicrophoneMutedFor("Elden Ring", byHotkey: true));
        Assert.True(config.MicrophoneMutedFor("Valorant", byHotkey: false));
    }

    [Fact]
    public void The_worker_is_told_whether_the_hotkey_muted_it()
    {
        var json = JsonSerializer.Serialize(new IpcMessage { Type = IpcType.Microphone, MicrophoneMuted = true }, CoreJson.Default.IpcMessage);
        Assert.Contains("\"type\":\"microphone\"", json);
        Assert.True(JsonSerializer.Deserialize(json, CoreJson.Default.IpcMessage)!.MicrophoneMuted);
    }
}
