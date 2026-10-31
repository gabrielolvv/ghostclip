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
    private readonly string _boundsFile;
    // Windows 11's acrylic behind the window: the page's translucent panels show the desktop blurred through.
    private bool _glass;

    public LibraryWindow(GhostclipPaths paths, GhostclipConfig config, ILogger log, string? clip)
    {
        _paths = paths;
        _log = log;
        Text = "Ghostclip";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(WindowBounds.MinWidth, WindowBounds.MinHeight);
        // Back where it was, unless that spot is off every screen now.
        _boundsFile = Path.Combine(paths.DataDir, "library-window.json");
        if (WindowBounds.Read(_boundsFile) is { } last && last.FitsOn(Screen.AllScreens.Select(s => s.WorkingArea)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(last.X, last.Y, last.Width, last.Height);
            if (last.Maximized) WindowState = FormWindowState.Maximized;
        }
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
        // Dark caption on Windows 10 20H1+.
        var on = 1;
        _ = DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref on, sizeof(int));
        // Windows 11 22H2+: acrylic under the whole window, caption included. Older Windows keeps the solid look.
        var acrylic = DwmBackdropAcrylic;
        _glass = Environment.OSVersion.Version.Build >= 22621 &&
            DwmSetWindowAttribute(Handle, DwmSystemBackdropType, ref acrylic, sizeof(int)) == 0;
        if (_glass)
        {
            var whole = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(Handle, ref whole);
            // Over the extended frame, black is what lets the acrylic through; the page paints its own panels on top.
            BackColor = Color.Black;
            _web.DefaultBackgroundColor = Color.Transparent;
        }
        else
        {
            var caption = 0x00100D0B; // COLORREF of #0B0D10: the caption matches the canvas
            _ = DwmSetWindowAttribute(Handle, DwmCaptionColor, ref caption, sizeof(int));
        }
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
            // In the right language from the first paint; a change later arrives as a "language" event.
            core.Navigate($"https://{AppHost}/index.html?lang={UiText.Code}{(_glass ? "&glass=1" : "")}");
        }
        catch (Exception ex) when (ex is COMException or WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Could not start the library page");
            MessageBox.Show(L($"The clip library couldn't start: {ex.Message}", $"A biblioteca de clipes não conseguiu abrir: {ex.Message}"),
                "Ghostclip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _bridge.HasRunningJobs &&
            MessageBox.Show(this, L("A trim or compression is still running. Close and stop it?",
                    "Um corte ou compressão ainda está rodando. Fechar e interromper?"), "Ghostclip",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        new WindowBounds(r.X, r.Y, r.Width, r.Height, WindowState == FormWindowState.Maximized).Write(_boundsFile);
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // Gone from the screen right away, while the bridge waits for FFmpeg runs to stop.
        Hide();
        _bridge.Dispose();
        base.OnFormClosed(e);
    }

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmSystemBackdropType = 38;
    private const int DwmBackdropAcrylic = 3; // DWMSBT_TRANSIENTWINDOW

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
}
