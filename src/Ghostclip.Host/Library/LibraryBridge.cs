using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ghostclip.Core;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace Ghostclip.Host.Library;

/// <summary>
/// Answers the page's requests (<see cref="LibraryRequest"/>) with events
/// (<see cref="LibraryEvent"/>). Every action on a file first checks that the
/// file is inside the clips folder: the page only ever names clips.
/// </summary>
internal sealed class LibraryBridge : IDisposable
{
    private readonly LibraryWindow _window;
    private readonly ILogger _log;
    private readonly ClipTools _tools;
    private readonly ClipMetaService _meta;
    private readonly SettingsService _settings;
    private readonly StatusWatcher _status;
    private readonly CancellationTokenSource _closing = new();
    private readonly System.Windows.Forms.Timer _changed = new() { Interval = 500 };
    private GhostclipConfig _config;
    private FileSystemWatcher? _watcher;
    private string? _rootError;
    private bool _listed;

    public LibraryBridge(LibraryWindow window, GhostclipPaths paths, GhostclipConfig config, ILogger log)
    {
        _window = window;
        _log = log;
        _config = config;
        Root = config.ResolvedClipsFolder;
        _tools = new ClipTools(paths, () => _config, log);
        _meta = new ClipMetaService(paths, _tools, log);
        _status = new StatusWatcher(window, paths);
        _settings = new SettingsService(window, paths, () => _config, () => Root, OnSettingsSaved, _status);
        Directory.CreateDirectory(ThumbsDir);

        // Folder changes arrive in bursts (a save is create, then rename); one "changed" per quiet half second.
        _changed.Tick += (_, _) =>
        {
            _changed.Stop();
            _window.Post(new LibraryEvent("changed"));
        };
        UseRoot();
    }

    public string Root { get; private set; }

    public bool RootAvailable { get; private set; }

    public string ThumbsDir => _meta.ThumbsDir;

    /// <summary>A clip (or <see cref="LibraryProgram.SettingsArgument"/>) to show once the page has its list.</summary>
    public string? PendingClip { get; set; }

    public void Open(string? clip)
    {
        if (clip is null) return;
        if (_listed) PostOpen(clip);
        else PendingClip = clip;
    }

    private void PostOpen(string clip) =>
        _window.Post(clip == LibraryProgram.SettingsArgument ? new LibraryEvent("showSettings") : new LibraryEvent("open", Path: clip));

    public void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Only the app's own page; anything else the WebView might show gets no access to clips.
        if (!LibraryOrigin.IsApp(e.Source)) return;
        LibraryRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(e.TryGetWebMessageAsString(), LibraryJson.Default.LibraryRequest);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            _log.Warning("Ignoring a malformed library message: {Error}", ex.Message);
            return;
        }
        if (request is not null) _ = HandleAsync(request);
    }

    private async Task HandleAsync(LibraryRequest r)
    {
        try
        {
            if (SettingsService.Handles(r.Type))
            {
                await _settings.HandleAsync(r).ConfigureAwait(true);
                return;
            }
            if (r.Type == "status")
            {
                _status.Push(always: true);
                return;
            }
            if (r.Type == "list")
            {
                await ListAsync().ConfigureAwait(true);
                return;
            }
            if (r.Type == "openFolder")
            {
                Process.Start(new ProcessStartInfo(Root) { UseShellExecute = true });
                return;
            }
            if (r.Path is not { } path || !RootAvailable || !ClipLibrary.IsInside(Root, path))
            {
                _window.Post(new LibraryEvent("error", Op: r.Type, Path: r.Path, Message: "That file isn't in your clips folder."));
                return;
            }

            switch (r.Type)
            {
                case "meta":
                    var (meta, thumb) = await _meta.GetAsync(path, _closing.Token).ConfigureAwait(true);
                    _window.Post(new LibraryEvent("meta", Path: path, Meta: meta,
                        Thumb: thumb is null ? null : $"https://{LibraryWindow.CacheHost}/{Path.GetFileName(thumb)}"));
                    break;
                case "trim":
                    var trimmed = await _tools.TrimAsync(path, r.Start, r.End).ConfigureAwait(true);
                    _window.Post(new LibraryEvent("done", Op: r.Type, Path: path, Output: trimmed));
                    break;
                case "compress":
                    var (compressed, bytes) = await _tools.CompressAsync(path).ConfigureAwait(true);
                    _window.Post(new LibraryEvent("done", Op: r.Type, Path: path, Output: compressed,
                        Message: string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024:0.0} MB")));
                    break;
                case "copy":
                    Clipboard.SetFileDropList(new StringCollection { path });
                    _window.Post(new LibraryEvent("done", Op: r.Type, Path: path));
                    break;
                case "reveal":
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    break;
                case "delete":
                    RecycleBin.Send(path, _window.Handle);
                    _window.Post(new LibraryEvent("done", Op: r.Type, Path: path));
                    break;
                default:
                    _log.Warning("Unknown library request {Type}", r.Type);
                    break;
            }
        }
        // The page waits for an answer to every action (its buttons stay busy until then), so
        // whatever goes wrong here goes back to it as a message.
        catch (Exception ex)
        {
            if (_closing.IsCancellationRequested) return;
            _log.Warning("Library {Op} failed for {Path}: {Error}", r.Type, r.Path, ex.Message);
            _window.Post(new LibraryEvent("error", Op: r.Type, Path: r.Path, Message: ex.Message));
        }
    }

    private async Task ListAsync()
    {
        if (RootAvailable)
        {
            var root = Root;
            var clips = await Task.Run(() => ClipLibrary.Scan(root)).ConfigureAwait(true);
            _window.Post(new LibraryEvent("clips", Clips: clips.Select(ClipDto.From).ToList(), Root: root));
        }
        else
        {
            _window.Post(new LibraryEvent("clips", Clips: [], Root: Root));
            _window.Post(new LibraryEvent("error", Op: "list", Message: $"Your clips folder isn't available: {_rootError}"));
        }
        if (!_listed) _status.Push(always: true);
        _listed = true;
        if (PendingClip is { } pending)
        {
            PendingClip = null;
            PostOpen(pending);
        }
    }

    // A new clips folder from the settings page shows up in this window right away.
    private void OnSettingsSaved(GhostclipConfig config)
    {
        var root = config.ResolvedClipsFolder;
        _config = config;
        if (string.Equals(Path.GetFullPath(root), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) return;
        Root = root;
        UseRoot();
        _window.MapClips();
        _window.Post(new LibraryEvent("changed"));
    }

    // An unplugged drive or a folder without permission: the library shows it empty and says why.
    private void UseRoot()
    {
        _watcher?.Dispose();
        _watcher = null;
        RootAvailable = ClipLibrary.TryPrepare(Root, out _rootError);
        if (!RootAvailable) return;
        try
        {
            _watcher = new FileSystemWatcher(Root, "*.mp4") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            _watcher.Created += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // Without a watcher the list just doesn't refresh by itself.
            _log.Warning("Not watching {Root}: {Error}", Root, ex.Message);
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        if (_window.IsDisposed || !_window.IsHandleCreated) return;
        _window.BeginInvoke(() =>
        {
            _changed.Stop();
            _changed.Start();
        });
    }

    public void Dispose()
    {
        _closing.Cancel();
        _watcher?.Dispose();
        _status.Dispose();
        _changed.Dispose();
        _closing.Dispose();
    }
}
