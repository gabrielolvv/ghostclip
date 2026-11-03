using System.Diagnostics;
using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Testers lost about 20 FPS while recording: by default the recorder now yields the CPU and GPU to the game.
public class RecorderPriorityTests
{
    [Fact]
    public void Games_come_first_by_default()
    {
        Assert.True(new GhostclipConfig().GamePriority);
        var fromOldFile = JsonSerializer.Deserialize("""{ "hotkey": "Alt+F10" }""", CoreJson.Default.GhostclipConfig)!;
        Assert.True(fromOldFile.GamePriority);
    }

    [Fact]
    public void Games_first_runs_the_recorder_below_normal_on_cpu_and_gpu()
    {
        var p = RecorderPriority.For(gamesFirst: true);
        Assert.Equal(ProcessPriorityClass.BelowNormal, p.Cpu);
        Assert.Equal(GpuPriorityClass.BelowNormal, p.Gpu);
    }

    [Fact]
    public void Turned_off_the_recorder_runs_at_normal_priority()
    {
        var p = RecorderPriority.For(gamesFirst: false);
        Assert.Equal(ProcessPriorityClass.Normal, p.Cpu);
        Assert.Equal(GpuPriorityClass.Normal, p.Gpu);
    }

    [Fact]
    public void Switching_it_applies_live_without_restarting_recording()
    {
        Assert.True(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { GamePriority = false }));
    }
}
