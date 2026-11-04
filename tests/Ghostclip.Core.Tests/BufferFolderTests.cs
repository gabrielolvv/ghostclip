using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The replay buffer writes to its drive all the time: it can live on another one.
public class BufferFolderTests
{
    private static readonly GhostclipPaths Paths = new(@"C:\Data");

    [Fact]
    public void Without_a_choice_it_stays_with_the_other_data()
    {
        var config = new GhostclipConfig().Normalize();
        Assert.Null(config.BufferFolder);
        Assert.Equal(@"C:\Data\Buffer", Paths.BufferDirFor(config));
        Assert.Equal(@"C:\Data\Pending", Paths.PendingDirFor(config));
    }

    [Fact]
    public void A_chosen_folder_gets_folders_of_our_own_inside_it()
    {
        // Clearing the buffer or uninstalling touches these, never what else is in the chosen folder.
        var config = new GhostclipConfig { BufferFolder = @"D:\Games" }.Normalize();
        Assert.Equal(@"D:\Games\GhostclipBuffer", Paths.BufferDirFor(config));
    }

    [Fact]
    public void Saves_stay_on_the_buffer_s_drive_so_they_can_hard_link()
    {
        var config = new GhostclipConfig { BufferFolder = @"D:\Games" }.Normalize();
        Assert.Equal(@"D:\Games\GhostclipPending", Paths.PendingDirFor(config));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_folder_means_no_choice(string folder)
    {
        Assert.Null(new GhostclipConfig { BufferFolder = folder }.Normalize().BufferFolder);
    }

    [Fact]
    public void Changing_it_restarts_recording_but_the_same_folder_in_other_case_doesn_t()
    {
        var a = new GhostclipConfig { BufferFolder = @"D:\Games" };
        Assert.False(a.SameCaptureAs(a with { BufferFolder = @"E:\Games" }));
        Assert.False(a.SameCaptureAs(a with { BufferFolder = null }));
        Assert.True(a.SameCaptureAs(a with { BufferFolder = @"d:\games" }));
    }

    [Fact]
    public void A_config_from_before_it_keeps_the_default()
    {
        Assert.Null(JsonSerializer.Deserialize("""{"hotkey":"Alt+F10"}""", CoreJson.Default.GhostclipConfig)!.Normalize().BufferFolder);
    }

    [Fact]
    public void The_worker_is_told_the_folder()
    {
        var json = JsonSerializer.Serialize(new GhostclipConfig { BufferFolder = @"D:\Games" }, CoreJson.Default.GhostclipConfig);
        Assert.Equal(@"D:\Games", JsonSerializer.Deserialize(json, CoreJson.Default.GhostclipConfig)!.BufferFolder);
    }
}
