using Ghostclip.Core;
using Xunit;

namespace Ghostclip.Core.Tests;

public class UpdatePolicyTests
{
    [Fact]
    public void Plain_launch_with_nothing_else_running_applies_a_downloaded_update_on_startup()
    {
        Assert.True(UpdatePolicy.ApplyOnStartup([], trayRunning: false, libraryOpen: false));
    }

    [Theory]
    [InlineData("--library")]
    [InlineData("--library", "--settings")]
    [InlineData("--library", @"C:\Clips\Game\Game_2026-10-30_21-15-33.mp4")]
    [InlineData("--trim", "clip.mp4", "1", "5")]
    [InlineData("--compress", "clip.mp4")]
    [InlineData("--save")]
    public void Other_launches_leave_the_update_for_the_tray(params string[] args)
    {
        // Applying stops every Ghostclip process, the recording tray included, and restarts only this one.
        Assert.False(UpdatePolicy.ApplyOnStartup(args, trayRunning: false, libraryOpen: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_plain_launch_leaves_it_while_Ghostclip_is_already_running(bool trayRunning, bool libraryOpen)
    {
        // The Start menu shortcut clicked while the tray records, or after Exit with the library still compressing.
        Assert.False(UpdatePolicy.ApplyOnStartup([], trayRunning, libraryOpen));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void Exit_applies_only_a_ready_update_with_the_library_closed(bool ready, bool libraryOpen, bool expected)
    {
        Assert.Equal(expected, UpdatePolicy.ApplyOnExit(ready, libraryOpen));
    }
}
