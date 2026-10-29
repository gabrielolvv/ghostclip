using System.Diagnostics;
using Ghostclip.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ghostclip.Host;

/// <summary>The tray app: icon and menu, hotkey, saving, and the worker's lifecycle.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly GhostclipPaths _paths;
    private readonly LoggingLevelSwitch _level;
    private readonly ILogger _log = Log.ForContext<TrayApp>();
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _saveItem;
    private readonly HotkeyWindow _hotkey = new();
    private readonly WorkerSupervisor _worker;
    private readonly ClipSaver _saver;
    private readonly ConfirmationSound _sound = new();
    private readonly EventWaitHandle _saveRequest = new(false, EventResetMode.AutoReset, Program.SaveEventName);
    private readonly RegisteredWaitHandle _saveRequestWait;
    private GhostclipConfig _config;
    private WorkerStatus _status = new() { State = WorkerState.Starting };
    private SettingsForm? _settings;
    private string? _lastClip;
    private DateTime _lastWarningUtc;

    public TrayApp(GhostclipPaths paths, GhostclipConfig config, string? configProblem, LoggingLevelSwitch level)
    {
        _paths = paths;
        _config = config;
        _level = level;
        _ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_ui);

        _saveItem = new ToolStripMenuItem("Save clip", null, (_, _) => SaveClip()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_saveItem);
        menu.Items.Add("Open clips folder", null, (_, _) => OpenFolder(_config.ResolvedClipsFolder));
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open logs folder", null, (_, _) => OpenFolder(_paths.LogsDir));
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _icon = new NotifyIcon { Icon = TrayIcons.Waiting, Text = "Ghostclip: starting", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => ShowSettings();
        _icon.BalloonTipClicked += (_, _) => RevealLastClip();

        _saver = new ClipSaver(paths, () => _config, Log.ForContext<ClipSaver>());
        _worker = new WorkerSupervisor(Log.ForContext<WorkerSupervisor>());
        _worker.StatusChanged += s => _ui.Post(_ => OnStatus(s), null);
        _worker.ErrorReported += (message, fatal) => _ui.Post(_ => OnWorkerError(message, fatal), null);
        _hotkey.Pressed += SaveClip;
        _saveRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _saveRequest, (_, _) => _ui.Post(_ => SaveClip(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        RegisterHotkey();
        if (configProblem is not null) Notify("Settings reset", configProblem, ToolTipIcon.Warning);
        _worker.Start(_config);
        _ = RecoverPendingAsync();
    }

    private void RegisterHotkey()
    {
        if (_hotkey.TryRegister(_config.Hotkey, out var error))
        {
            _saveItem.Text = $"Save clip ({_config.Hotkey})";
        }
        else
        {
            _saveItem.Text = "Save clip";
            Notify("Hotkey not available", $"{error} Pick another one in Settings.", ToolTipIcon.Warning);
        }
    }

    private void SaveClip()
    {
        var game = ForegroundGame.Detect();
        ClipSnapshot snapshot;
        try
        {
            snapshot = _saver.TakeSnapshot(game, recording: _status.State == WorkerState.Recording);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Warning("Save failed: {Error}", ex.Message);
            Notify("Clip not saved", ex.Message, ToolTipIcon.Warning);
            return;
        }
        // The clip is safe from here on: confirm right away, the MP4 follows.
        if (_config.ConfirmationSound) _sound.Play();
        _ = FinishAsync(snapshot);
    }

    private async Task FinishAsync(ClipSnapshot snapshot)
    {
        try
        {
            var path = await Task.Run(() => _saver.RemuxAsync(snapshot)).ConfigureAwait(true);
            _lastClip = path;
            Notify("Clip saved", $"{snapshot.Game}: {snapshot.Seconds:0} s. Click to show it.", ToolTipIcon.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Error("Remux failed: {Error}", ex.Message);
            Notify("Clip not saved", ex.Message, ToolTipIcon.Error);
        }
    }

    // Snapshots a previous run took but never turned into MP4s (crash, shutdown).
    private async Task RecoverPendingAsync()
    {
        foreach (var snapshot in _saver.PendingFromEarlierRuns())
        {
            _log.Information("Finishing a clip from an earlier run: {Dir}", snapshot.Dir);
            await FinishAsync(snapshot).ConfigureAwait(true);
        }
    }

    private void OnStatus(WorkerStatus status)
    {
        _status = status;
        var (icon, text) = status.State switch
        {
            WorkerState.Recording => (TrayIcons.Recording, $"Ghostclip: recording ({status.Encoder}, {status.Fps:0} fps{(status.Audio ? ", audio" : "")})"),
            WorkerState.Stopped => (TrayIcons.Stopped, "Ghostclip: not recording"),
            WorkerState.Probing => (TrayIcons.Waiting, "Ghostclip: detecting the encoder"),
            _ => (TrayIcons.Waiting, "Ghostclip: starting"),
        };
        if (_icon.Icon != icon) _icon.Icon = icon;
        // NotifyIcon.Text is capped at 127 characters.
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void OnWorkerError(string message, bool fatal)
    {
        if (fatal)
        {
            Notify("Ghostclip is not recording", message, ToolTipIcon.Error);
            return;
        }
        // Restarts can come in bursts (monitor unplugged): one balloon a minute.
        if (DateTime.UtcNow - _lastWarningUtc < TimeSpan.FromMinutes(1)) return;
        _lastWarningUtc = DateTime.UtcNow;
        Notify("Recording interrupted", "Ghostclip is restarting the recorder. The buffer is kept.", ToolTipIcon.Warning);
    }

    private void ShowSettings()
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(_config, ApplyConfig);
        _settings.FormClosed += (_, _) =>
        {
            _settings.Dispose();
            _settings = null;
        };
        _settings.Show();
    }

    private void ApplyConfig(GhostclipConfig config)
    {
        var hotkeyChanged = config.Hotkey != _config.Hotkey;
        _config = config;
        ConfigStore.Save(_paths.ConfigFile, config);
        _level.MinimumLevel = Program.ToSerilog(config.LogLevel);
        if (hotkeyChanged) RegisterHotkey();
        _worker.UpdateConfig(config);
        _log.Information("Settings saved");
    }

    private void Notify(string title, string text, ToolTipIcon icon) =>
        _icon.ShowBalloonTip(4000, title, text, icon);

    private void RevealLastClip()
    {
        if (_lastClip is not null && File.Exists(_lastClip))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastClip}\"") { UseShellExecute = false });
        }
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Exit()
    {
        _icon.Visible = false;
        _hotkey.Dispose();
        _saveRequestWait.Unregister(null);
        _saveRequest.Dispose();
        _settings?.Close();
        // Give the worker a moment to close its last segment cleanly.
        Task.Run(async () => await _worker.DisposeAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(8));
        _sound.Dispose();
        _icon.Dispose();
        ExitThread();
    }
}
