using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The buffer is the last moments of the screen: it shouldn't outlast Ghostclip unless asked to.
public class ClearBufferOnExitTests
{
    [Fact]
    public void It_is_on_by_default()
    {
        Assert.True(new GhostclipConfig().Normalize().ClearBufferOnExit);
    }

    [Fact]
    public void A_config_from_before_it_gets_the_default()
    {
        Assert.True(JsonSerializer.Deserialize("""{"hotkey":"Alt+F10"}""", CoreJson.Default.GhostclipConfig)!.Normalize().ClearBufferOnExit);
    }

    [Fact]
    public void Turning_it_off_is_kept_and_reaches_the_worker()
    {
        var json = JsonSerializer.Serialize(new GhostclipConfig { ClearBufferOnExit = false }, CoreJson.Default.GhostclipConfig);
        Assert.False(JsonSerializer.Deserialize(json, CoreJson.Default.GhostclipConfig)!.Normalize().ClearBufferOnExit);
    }

    [Fact]
    public void Changing_it_keeps_recording()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { ClearBufferOnExit = false }));
    }
}
