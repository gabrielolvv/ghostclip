using System.Runtime.InteropServices;
using System.Text.Json;
using Ghostclip.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace Ghostclip.Host.Library;

/// <summary>
/// The library window: a native frame painted dark, filled by a WebView2 that
/// shows the page in Library\wwwroot. Clips and thumbnails reach the page
/// through virtual hosts, so the video element streams straight from disk.
/// </summary>
internal sealed partial class LibraryWindow : Form
{
    public const string AppHost = LibraryOrigin.AppHost;
    public const string ClipsHost = "clips.ghostclip";
    public const string CacheHost = "cache.ghostclip";

    private static readonly Color Canvas = Color.FromArgb(0x0B, 0x0D, 0x10);

    private readonly GhostclipPaths _paths;
    private readonly ILogger _log;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Canvas };
    private readonly LibraryBridge _bridge;

    public LibraryWindow(GhostclipPaths paths, GhostclipConfig config, ILogger log, string? clip)
    {
        _paths = paths;
        _log = log;
        Text = "Ghostclip";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        BackColor = Canvas;
        Controls.Add(_web);
        _bridge = new LibraryBridge(this, paths, config, log) { PendingClip = clip };
    }

    public static bool RuntimeAvailable()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() is not null;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Brings the window forward and shows <paramref name="clip"/>, if any.</summary>
    public void ShowClip(string? clip)
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        _bridge.Open(clip);
    }

    /// <summary>Points the clips virtual host at the bridge's clips folder, again after it changes.</summary>
    internal void MapClips()
    {
        if (_web.CoreWebView2 is not { } core) return;
        core.ClearVirtualHostNameToFolderMapping(ClipsHost);
        if (_bridge.RootAvailable)
        {
            core.SetVirtualHostNameToFolderMapping(ClipsHost, _bridge.Root, CoreWebView2HostResourceAccessKind.Allow);
        }
    }

    internal void Post(LibraryEvent message)
    {
        if (_web.CoreWebView2 is null || IsDisposed) return;
        _web.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(message, LibraryJson.Default.LibraryEvent));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark caption on Windows 10 20H1+; the exact canvas color on Windows 11.
        var on = 1;
        _ = DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref on, sizeof(int));
        var caption = 0x00100D0B; // COLORREF of #0B0D10
        _ = DwmSetWindowAttribute(Handle, DwmCaptionColor, ref caption, sizeof(int));
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(_paths.DataDir, "WebView")).ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
            // The window only ever shows the library page: a file dropped on it, a link or a
            // window.open would otherwise replace the page and leave no way back.
            _web.AllowExternalDrop = false;
            core.NavigationStarting += (_, args) => { if (!LibraryOrigin.IsApp(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "Library", "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            MapClips();
            core.SetVirtualHostNameToFolderMapping(CacheHost, _bridge.ThumbsDir, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += _bridge.OnMessage;
            core.Navigate($"https://{AppHost}/index.html");
        }
        catch (Exception ex) when (ex is COMException or WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Could not start the library page");
            MessageBox.Show($"The clip library couldn't start: {ex.Message}", "Ghostclip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _bridge.Dispose();
        base.OnFormClosed(e);
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
