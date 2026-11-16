using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// "Copy new clips": each clip or screenshot saved goes on the clipboard, ready to paste. It takes over the
// clipboard, so it's off until people turn it on.
public class CopyNewClipsTests
{
    [Fact]
    public void It_starts_off() => Assert.False(new GhostclipConfig().Normalize().CopyNewClips);

    [Fact]
    public void A_settings_file_from_before_it_has_it_off() =>
        Assert.False(JsonSerializer.Deserialize("""{"hotkey":"Alt+F10"}""", CoreJson.Default.GhostclipConfig)!.Normalize().CopyNewClips);

    [Fact]
    public void Turned_on_it_stays_on()
    {
        var json = JsonSerializer.Serialize(new GhostclipConfig { CopyNewClips = true }, CoreJson.Default.GhostclipConfig);
        Assert.Contains("\"copyNewClips\":true", json);
        Assert.True(JsonSerializer.Deserialize(json, CoreJson.Default.GhostclipConfig)!.Normalize().CopyNewClips);
    }
}
