using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// What testers paste back after an FPS test: enough to tell which GPU, capture path and priority ran.
public class DiagnosticsReportTests
{
    private static readonly string[] Log =
    [
        "2026-11-03 10:00:01.000 [INF] Worker 0.8.4.0 starting (pid 100)",
        "2026-11-03 10:00:02.000 [INF] Recording with h264_nvenc (direct), 60 fps, 20 Mbps, audio tracks [Game], from segment 0",
        "2026-11-03 10:00:02.010 [INF] Recorder priority: CPU Normal, GPU Normal",
        "2026-11-03 10:05:00.000 [INF] Recording with h264_nvenc (direct, graphics capture), 60 fps, 20 Mbps, audio tracks [Game], from segment 0",
        "2026-11-03 10:05:00.010 [INF] Recorder priority: CPU BelowNormal, GPU BelowNormal",
    ];

    private static string Report(IReadOnlyList<string> log, params GpuInfo[] gpus) =>
        DiagnosticsReport.Build("0.8.4", "Microsoft Windows 10.0.22631", gpus,
            new GhostclipConfig { Capture = CaptureMethod.GraphicsCapture }, log);

    [Fact]
    public void It_names_the_version_windows_and_each_gpu_with_its_display()
    {
        var text = Report(Log,
            new GpuInfo("NVIDIA GeForce RTX 3060", "32.0.15.6094", 2560, 1440, 144),
            new GpuInfo("Intel(R) UHD Graphics", "32.0.101.5972", 0, 0, 0));

        Assert.StartsWith("Ghostclip 0.8.4", text);
        Assert.Contains("Windows: Microsoft Windows 10.0.22631", text);
        Assert.Contains("GPU: NVIDIA GeForce RTX 3060, driver 32.0.15.6094, 2560x1440 @ 144 Hz", text);
        Assert.Contains("GPU: Intel(R) UHD Graphics, driver 32.0.101.5972", text);
        Assert.DoesNotContain("0x0", text);
    }

    [Fact]
    public void It_lists_the_settings_that_decide_the_cost()
    {
        var text = Report(Log);
        Assert.Contains("Settings: capture GraphicsCapture, games first on, encoder Auto, input Auto, H264, 60 fps, 20 Mbps, resolution monitor", text);
    }

    [Fact]
    public void It_quotes_the_latest_recording_and_priority_lines_without_their_timestamps()
    {
        var text = Report(Log);
        Assert.Contains("Recording with h264_nvenc (direct, graphics capture), 60 fps", text);
        Assert.Contains("Recorder priority: CPU BelowNormal, GPU BelowNormal", text);
        Assert.DoesNotContain("CPU Normal, GPU Normal", text);
        Assert.DoesNotContain("[INF]", text);
    }

    [Fact]
    public void Without_a_recording_in_the_log_it_says_so()
    {
        var text = Report([]);
        Assert.Contains("Recording: not in today's log", text);
    }
}
