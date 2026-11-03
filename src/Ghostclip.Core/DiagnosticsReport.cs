using System.Globalization;
using System.Text;

namespace Ghostclip.Core;

/// <summary>A display adapter; width, height and refresh are 0 for one that drives no display.</summary>
public sealed record GpuInfo(string Name, string? Driver, int Width, int Height, int RefreshHz);

/// <summary>
/// The text "Copy diagnostics" puts on the clipboard: what a tester pastes back after
/// an FPS test, so the GPU, the capture path and the priority that ran are known
/// without anyone opening a log. English, like the logs it quotes.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(string appVersion, string os, IReadOnlyList<GpuInfo> gpus, GhostclipConfig config, IReadOnlyList<string> workerLog)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Ghostclip {appVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Windows: {os}");
        foreach (var gpu in gpus)
        {
            sb.Append(CultureInfo.InvariantCulture, $"GPU: {gpu.Name}");
            if (!string.IsNullOrWhiteSpace(gpu.Driver)) sb.Append(CultureInfo.InvariantCulture, $", driver {gpu.Driver}");
            if (gpu.Width > 0 && gpu.Height > 0) sb.Append(CultureInfo.InvariantCulture, $", {gpu.Width}x{gpu.Height} @ {gpu.RefreshHz} Hz");
            sb.AppendLine();
        }
        var resolution = config.OutputHeight > 0 ? $"{config.OutputHeight}p" : "monitor";
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Settings: capture {config.Capture}, games first {(config.GamePriority ? "on" : "off")}, encoder {config.Encoder}, input {config.Input}, " +
            $"{config.Codec}, {config.Fps} fps, {config.BitrateMbps} Mbps, resolution {resolution}");
        sb.AppendLine(Latest(workerLog, "Recording with ") ?? "Recording: not in today's log");
        if (Latest(workerLog, "Recorder priority:") is { } priority) sb.AppendLine(priority);
        return sb.ToString().TrimEnd();
    }

    // The message of the newest log line that starts with it, without the timestamp and level.
    private static string? Latest(IReadOnlyList<string> log, string start)
    {
        for (var i = log.Count - 1; i >= 0; i--)
        {
            var line = log[i];
            var end = line.IndexOf("] ", StringComparison.Ordinal);
            var message = end >= 0 ? line[(end + 2)..] : line;
            if (message.StartsWith(start, StringComparison.Ordinal)) return message;
        }
        return null;
    }
}
