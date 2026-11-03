using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host.Library;

/// <summary>Gathers what <see cref="DiagnosticsReport"/> puts on the clipboard. Off the UI thread: WMI can take a second.</summary>
internal static class Diagnostics
{
    public static string Collect(GhostclipPaths paths, GhostclipConfig config, ILogger log)
    {
        var version = typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        // "0.8.4+commit": the commit says nothing to a tester.
        version = version.Split('+')[0];
        return DiagnosticsReport.Build(version, RuntimeInformation.OSDescription, Gpus(log), config, WorkerLog(paths, log));
    }

    private static IReadOnlyList<GpuInfo> Gpus(ILogger log)
    {
        var gpus = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
            foreach (var gpu in searcher.Get())
            {
                using (gpu)
                {
                    gpus.Add(new GpuInfo(gpu["Name"] as string ?? "?", gpu["DriverVersion"] as string,
                        Number(gpu["CurrentHorizontalResolution"]), Number(gpu["CurrentVerticalResolution"]), Number(gpu["CurrentRefreshRate"])));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            log.Warning("Could not read the GPUs for diagnostics: {Error}", ex.Message);
        }
        return gpus;
    }

    private static int Number(object? value) => value is null ? 0 : (int)Math.Min(int.MaxValue, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));

    // Today's worker log, read while the worker keeps writing it.
    private static IReadOnlyList<string> WorkerLog(GhostclipPaths paths, ILogger log)
    {
        try
        {
            var file = new DirectoryInfo(paths.LogsDir).EnumerateFiles("worker-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (file is null) return [];
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning("Could not read the worker log for diagnostics: {Error}", ex.Message);
            return [];
        }
    }
}
