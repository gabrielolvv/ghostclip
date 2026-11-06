using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A key that marks a moment in a session without saving a clip. Optional, like the short clip and pause keys.
public class BookmarkKeyTests
{
    [Fact]
    public void It_is_off_until_chosen()
    {
        var config = new GhostclipConfig().Normalize();
        Assert.Equal("", config.BookmarkHotkey);
        Assert.False(config.HasBookmarkHotkey);
        Assert.DoesNotContain("", config.ChosenHotkeys);
    }

    [Fact]
    public void Its_hotkey_comes_last_in_the_hotkey_list()
    {
        // The tray finds each action's key by its position here.
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9", "Alt+F8", "", "", "Alt+F6"], new GhostclipConfig { BookmarkHotkey = " Alt+F6 " }.Normalize().Hotkeys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("Ctrl+C")]
    public void No_key_or_a_common_shortcut_means_none(string? key)
    {
        Assert.False(new GhostclipConfig { BookmarkHotkey = key! }.Normalize().HasBookmarkHotkey);
    }

    [Theory]
    [InlineData("Alt+F10")]
    [InlineData("Alt+F11")]
    [InlineData("Alt+F9")]
    [InlineData("Alt+F8")]
    public void A_key_another_action_uses_goes_back_to_none(string key)
    {
        Assert.Equal("", new GhostclipConfig { BookmarkHotkey = key }.Normalize().BookmarkHotkey);
    }

    [Fact]
    public void The_optional_keys_dont_share_either()
    {
        Assert.Equal("", new GhostclipConfig { ShortClipHotkey = "Alt+F6", BookmarkHotkey = "Alt+F6" }.Normalize().BookmarkHotkey);
        Assert.Equal("", new GhostclipConfig { PauseHotkey = "Alt+F6", BookmarkHotkey = "Alt+F6" }.Normalize().BookmarkHotkey);
    }

    [Fact]
    public void A_key_is_listed_when_chosen_and_changing_it_doesnt_touch_recording()
    {
        Assert.Contains("Alt+F6", new GhostclipConfig { BookmarkHotkey = "Alt+F6" }.Normalize().ChosenHotkeys);
        Assert.True(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { BookmarkHotkey = "Alt+F6" }));
    }

    [Fact]
    public void A_command_does_it_too()
    {
        Assert.Equal("--bookmark", TrayCommands.Argument(TrayCommand.Bookmark));
        Assert.Equal(TrayCommand.Bookmark, TrayCommands.Parse("--bookmark"));
    }
}
