namespace Ghostclip.Core;

/// <summary>
/// A monitor the settings page offers. <see cref="Index"/> is what <see cref="GhostclipConfig.Monitor"/> stores:
/// the Desktop Duplication output on the GPU FFmpeg opens, then the other GPUs' outputs, which only Graphics
/// Capture records (<see cref="OtherGpu"/>). <see cref="Name"/> is null when Windows has none (a laptop's own
/// panel, usually). Left and Top place it on the desktop, which tells two identical monitors apart.
/// </summary>
public sealed record MonitorInfo(int Index, string? Name, int Left, int Top, int Width, int Height, bool Primary, bool BuiltIn, bool OtherGpu = false);

/// <summary>A DXGI output: its GDI device name (\\.\DISPLAY1), place on the desktop, and whether a GPU other than FFmpeg's drives it.</summary>
public readonly record struct DisplayOutput(int Index, string DeviceName, int Left, int Top, int Width, int Height, bool Primary, bool OtherGpu = false);

/// <summary>A monitor behind a GDI device, from the display configuration; Width and Height are 0 when unknown.</summary>
public readonly record struct DisplayTarget(string DeviceName, string? FriendlyName, bool BuiltIn, int Width, int Height);

public static class MonitorList
{
    /// <summary>Names each output after the monitor behind it. Duplicated screens take the first name they have.</summary>
    public static IReadOnlyList<MonitorInfo> Build(IEnumerable<DisplayOutput> outputs, IEnumerable<DisplayTarget> targets)
    {
        var byDevice = targets.ToLookup(t => t.DeviceName, StringComparer.OrdinalIgnoreCase);
        return outputs.Select(o =>
        {
            var behind = byDevice[o.DeviceName].ToList();
            var name = behind.Select(t => t.FriendlyName?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            // The mode Windows reports, in physical pixels; the desktop rectangle when there is none.
            var sized = behind.FirstOrDefault(t => t.Width > 0 && t.Height > 0);
            var (width, height) = sized.Width > 0 ? (sized.Width, sized.Height) : (o.Width, o.Height);
            return new MonitorInfo(o.Index, name, o.Left, o.Top, width, height, o.Primary, behind.Any(t => t.BuiltIn), o.OtherGpu);
        }).ToList();
    }
}
