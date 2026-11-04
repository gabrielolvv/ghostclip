namespace Ghostclip.Core;

/// <summary>Says when a drive runs low, once per drive until it has room again.</summary>
public sealed class LowDiskWatch
{
    private const long Floor = 2L * 1024 * 1024 * 1024;
    private const long BytesPerSecondPerMbps = 125_000;
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Room for the buffer and a couple of clips at these settings, and never less than 2 GB.</summary>
    public static long Threshold(GhostclipConfig config) =>
        Math.Max(Floor, 3 * config.BitrateMbps * BytesPerSecondPerMbps * config.MaxClipSeconds);

    /// <summary>True the first time a drive falls below the threshold. It can warn again only after recovering to twice that.</summary>
    public bool ShouldWarn(string drive, long freeBytes, long threshold)
    {
        if (freeBytes >= threshold * 2) _warned.Remove(drive);
        return freeBytes < threshold && _warned.Add(drive);
    }
}
