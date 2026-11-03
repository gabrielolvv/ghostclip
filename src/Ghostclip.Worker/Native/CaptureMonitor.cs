using Ghostclip.Core;
using Ghostclip.Native;
using Serilog;

namespace Ghostclip.Worker.Native;

/// <summary>The monitor Graphics Capture records: the one behind Desktop Duplication output <see cref="GhostclipConfig.Monitor"/>.</summary>
internal static class CaptureMonitor
{
    /// <summary>Its HMONITOR, or 0 (Graphics Capture then counts monitors its own way) for the other methods or when it can't be found.</summary>
    public static long HandleFor(GhostclipConfig config, ILogger log)
    {
        if (config.Capture != CaptureMethod.GraphicsCapture) return 0;
        try
        {
            var handle = Displays.Handle(config.Monitor);
            if (handle == 0) log.Warning("Monitor {Monitor} is not an output of the first GPU; Graphics Capture picks by its own numbering", config.Monitor);
            return handle;
        }
        catch (Exception ex)
        {
            log.Warning("Could not find monitor {Monitor} for Graphics Capture: {Error}", config.Monitor, ex.Message);
            return 0;
        }
    }
}
