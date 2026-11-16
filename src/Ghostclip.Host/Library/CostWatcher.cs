using System.Diagnostics;
using Ghostclip.Core;

namespace Ghostclip.Host.Library;

/// <summary>
/// Tells the page what recording uses now, every two seconds while the window is up: the tray, the recorder and
/// its FFmpeg. The library window itself isn't counted, since it only runs while people look at it. The GPU isn't
/// measured.
/// </summary>
internal sealed class CostWatcher : IDisposable
{
    private readonly LibraryWindow _window;
    private readonly Func<string> _bufferDir;
    private readonly Func<string> _ffmpeg;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private IReadOnlyList<CostSample>? _last;
    private DateTime _lastAt;

    public CostWatcher(LibraryWindow window, Func<string> bufferDir, Func<string> ffmpeg)
    {
        _window = window;
        _bufferDir = bufferDir;
        _ffmpeg = ffmpeg;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if (_window.IsDisposed || _window.WindowState == FormWindowState.Minimized)
        {
            // Not looked at: the next look starts over, rather than spreading the CPU used meanwhile over minutes.
            _last = null;
            return;
        }
        var now = DateTime.UtcNow;
        var samples = Sample();
        double? cpu = _last is null ? null : CostMeter.CpuPercent(_last, samples, now - _lastAt, Environment.ProcessorCount);
        _last = samples;
        _lastAt = now;
        double? disk;
        try
        {
            disk = CostMeter.DiskMBPerMinute(SegmentRing.Scan(_bufferDir()), now, GhostclipConfig.SegmentSeconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            disk = null;
        }
        // No tray running: nothing is recording, and nothing to show.
        _window.Post(new LibraryEvent("cost", Cost: samples.Count == 0 ? null : new LiveCost(cpu, samples.Sum(s => s.MemoryBytes), disk)));
    }

    private IReadOnlyList<CostSample> Sample()
    {
        var samples = new List<CostSample>();
        var self = Environment.ProcessId;
        var ffmpeg = SafeFullPath(_ffmpeg());
        foreach (var name in new[] { "Ghostclip", "Ghostclip.Worker", "ffmpeg" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == self) continue;
                        // Another program's FFmpeg isn't ours to count.
                        if (name == "ffmpeg" && !string.Equals(SafeFullPath(process.MainModule?.FileName), ffmpeg, StringComparison.OrdinalIgnoreCase)) continue;
                        samples.Add(new CostSample(process.Id, process.TotalProcessorTime, process.WorkingSet64));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        // Ended while being looked at, or not ours to read.
                    }
                }
            }
        }
        return samples;
    }

    private static string? SafeFullPath(string? path)
    {
        try
        {
            return path is null ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    public void Dispose() => _timer.Dispose();
}
