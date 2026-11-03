using Ghostclip.Core;
using Ghostclip.Native;
using Serilog;

namespace Ghostclip.Worker.Native;

/// <summary>The monitor Graphics Capture records: the one behind output <see cref="GhostclipConfig.Monitor"/>.</summary>
internal static class CaptureMonitor
{
    // Said once per change, not on every probe and restart.
    private static string? _lastSaid;

    /// <summary>Its HMONITOR, or 0 (Graphics Capture then counts monitors its own way) for the other methods or when it can't be found.</summary>
    public static long HandleFor(GhostclipConfig config, ILogger log)
    {
        if (config.Capture != CaptureMethod.GraphicsCapture) return 0;
        try
        {
            var (handle, device) = Displays.Handle(config.Monitor);
            Say(log, device is null
                ? $"Monitor {config.Monitor} is not connected; Graphics Capture picks by its own numbering"
                : $"Graphics Capture records monitor {config.Monitor} ({device})", warning: device is null);
            return handle;
        }
        catch (Exception ex)
        {
            Say(log, $"Could not find monitor {config.Monitor} for Graphics Capture: {ex.Message}", warning: true);
            return 0;
        }
    }

    private static void Say(ILogger log, string message, bool warning)
    {
        if (Interlocked.Exchange(ref _lastSaid, message) == message) return;
        if (warning) log.Warning("{Message}", message);
        else log.Information("{Message}", message);
    }
}
