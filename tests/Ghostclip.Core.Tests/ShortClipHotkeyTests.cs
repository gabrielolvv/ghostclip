using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A second hotkey that saves a clip of its own length. It takes no key until someone chooses one.
public class ShortClipHotkeyTests
{
    [Fact]
    public void It_takes_no_key_until_one_is_chosen()
    {
        var config = new GhostclipConfig().Normalize();
        Assert.False(config.HasShortClipHotkey);
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8"], config.ChosenHotkeys);
    }

    [Fact]
    public void A_chosen_key_comes_last_in_the_hotkey_list()
    {
        var config = new GhostclipConfig { ShortClipHotkey = " Alt+F7 " }.Normalize();
        Assert.True(config.HasShortClipHotkey);
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8", "Alt+F7"], config.Hotkeys);
        Assert.Equal(config.Hotkeys, config.ChosenHotkeys);
    }

    [Fact]
    public void Without_a_key_it_keeps_its_place_in_the_hotkey_list()
    {
        // The tray finds each action's key by its position here.
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8", ""], new GhostclipConfig().Normalize().Hotkeys);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("Ctrl+V")]
    public void A_blank_key_or_a_common_shortcut_leaves_it_off(string? key)
    {
        // The other hotkeys go back to their default; this one has none to go back to.
        Assert.Equal("", new GhostclipConfig { ShortClipHotkey = key! }.Normalize().ShortClipHotkey);
    }

    [Theory]
    [InlineData("Alt+F10")]
    [InlineData("f10 + alt")]
    [InlineData("Alt+F8")]
    public void On_a_key_another_action_uses_it_goes_back_to_off(string key)
    {
        // Only a hand-edited file gets here: Settings refuses it. The other action keeps its key.
        var config = new GhostclipConfig { ShortClipHotkey = key }.Normalize();
        Assert.False(config.HasShortClipHotkey);
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8"], config.ChosenHotkeys);
    }

    [Fact]
    public void A_moved_hotkey_never_lands_on_it()
    {
        // The screenshot hotkey on the clip's key moves to the first free spare: Alt+F12 is this one's here.
        var config = new GhostclipConfig { Hotkey = "Alt+F11", ShortClipHotkey = "Alt+F12" }.Normalize();
        Assert.Equal("Alt+F12", config.ShortClipHotkey);
        Assert.Equal("Ctrl+Alt+F11", config.ScreenshotHotkey);
    }

    [Fact]
    public void Its_length_stays_in_the_range_of_a_clip()
    {
        Assert.Equal(15, new GhostclipConfig().Normalize().ShortClipSeconds);
        Assert.Equal(10, new GhostclipConfig { ShortClipSeconds = 3 }.Normalize().ShortClipSeconds);
        Assert.Equal(600, new GhostclipConfig { ShortClipSeconds = 5000 }.Normalize().ShortClipSeconds);
    }

    [Fact]
    public void The_buffer_holds_a_longer_one_only_while_it_has_a_key()
    {
        Assert.Equal(60, new GhostclipConfig { ShortClipSeconds = 120 }.MaxClipSeconds);
        Assert.Equal(120, new GhostclipConfig { ShortClipHotkey = "Alt+F7", ShortClipSeconds = 120 }.MaxClipSeconds);
        Assert.Equal(60, new GhostclipConfig { ShortClipHotkey = "Alt+F7" }.MaxClipSeconds);
    }

    [Fact]
    public void The_worker_sizes_its_buffer_from_what_the_pipe_carries()
    {
        var json = JsonSerializer.Serialize(new GhostclipConfig { ShortClipHotkey = "Alt+F7", ShortClipSeconds = 120 }, CoreJson.Default.GhostclipConfig);
        Assert.Equal(120, JsonSerializer.Deserialize(json, CoreJson.Default.GhostclipConfig)!.MaxClipSeconds);
        Assert.DoesNotContain("hasShortClipHotkey", json);
        Assert.DoesNotContain("chosenHotkeys", json);
    }

    [Fact]
    public void A_config_from_before_this_hotkey_leaves_it_off()
    {
        var config = JsonSerializer.Deserialize("""{"hotkey":"Alt+F10","clipSeconds":60}""", CoreJson.Default.GhostclipConfig)!.Normalize();
        Assert.False(config.HasShortClipHotkey);
        Assert.Equal(15, config.ShortClipSeconds);
    }

    [Fact]
    public void Changing_it_keeps_recording()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { ShortClipHotkey = "Alt+F7", ShortClipSeconds = 120 }));
    }
}
