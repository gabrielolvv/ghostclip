using System.Diagnostics;
using Ghostclip.Core;

namespace Ghostclip.Host.Library;

/// <summary>
/// Follows the tray's status.json for the badge while the library is open.
/// A file whose process is gone (the tray crashed) counts as not running.
/// </summary>
internal sealed class StatusWatcher : IDisposable
{
    private readonly LibraryWindow _window;
    private readonly string _path;
    private readonly FileSystemWatcher? _watcher;
    // Catches a tray that died without deleting the file; only while the window is open.
    private readonly System.Windows.Forms.Timer _check = new() { Interval = 5000 };
    private RecorderStatus? _sent;

    public StatusWatcher(LibraryWindow window, GhostclipPaths paths)
    {
        _window = window;
        _path = paths.StatusFile;
        _check.Tick += (_, _) => Push();
        _check.Start();
        try
        {
            _watcher = new FileSystemWatcher(paths.DataDir, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // The timer still updates the badge, just less promptly.
        }
    }

    public RecorderStatus? Current
    {
        get
        {
            var status = StatusFile.Read(_path);
            return status is not null && TrayRunning(status.ProcessId) ? status : null;
        }
    }

    /// <summary>Sends the badge to the page if it changed.</summary>
    public void Push(bool always = false)
    {
        var status = Current;
        if (!always && status == _sent) return;
        _sent = status;
        _window.Post(new LibraryEvent("status", Status: status));
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_window.IsDisposed || !_window.IsHandleCreated) return;
        _window.BeginInvoke(() => Push());
    }

    private static bool TrayRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.ProcessName.Equals("Ghostclip", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _check.Dispose();
    }
}
