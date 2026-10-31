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
    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _updateItem = new() { Visible = false };
    private readonly ToolStripMenuItem _libraryItem;
    private readonly ToolStripMenuItem _clipsFolderItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _logsItem;
    private readonly ToolStripMenuItem _exitItem;
    // The registered hotkey as the menu names it (null when none is), and the update waiting to be applied.
    private string? _hotkeyLabel;
    private string? _updateVersion;
    private readonly UpdateService _updates = new(Log.ForContext<UpdateService>());
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 2 * 60 * 1000 };
    private readonly HotkeyWindow _hotkey = new();
    private readonly WorkerSupervisor _worker;
    private readonly ClipSaver _saver;
    private readonly System.Windows.Forms.Timer _foregroundTimer = new() { Interval = 2000 };
    private string _foreground = ClipNaming.FallbackGame;
    private string _game = ClipNaming.FallbackGame;
    private uint _gamePid;
    private readonly ConfirmationSound _sound = new();
    private readonly EventWaitHandle _saveRequest = new(false, EventResetMode.AutoReset, Program.SaveEventName);
    private readonly RegisteredWaitHandle _saveRequestWait;
    private readonly EventWaitHandle _exitRequest = new(false, EventResetMode.AutoReset, Program.ExitEventName);
    private readonly RegisteredWaitHandle _exitRequestWait;
    private bool _shuttingDown;
    private GhostclipConfig _config;
    private WorkerStatus _status = new() { State = WorkerState.Starting };
    // The settings page (in the library) writes config.json; this applies it, and hand edits too.
    private readonly FileSystemWatcher _configWatcher;
    private readonly System.Windows.Forms.Timer _configReload = new() { Interval = 300 };
    private int _configRetries;
    private string? _configError;
    private RecorderStatus? _published;
    private string? _lastClip;
    private DateTime _lastWarningUtc;

    public TrayApp(GhostclipPaths paths, GhostclipConfig config, string? configProblem, LoggingLevelSwitch level)
    {
        _paths = paths;
        _config = config;
        _level = level;
        _ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_ui);

        // Texts come from ApplyTexts, in the chosen language.
        _saveItem = new ToolStripMenuItem("", null, (_, _) => SaveClip()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _libraryItem = new ToolStripMenuItem("", null, (_, _) => OpenLibrary());
        _clipsFolderItem = new ToolStripMenuItem("", null, (_, _) => OpenFolder(_config.ResolvedClipsFolder));
        _settingsItem = new ToolStripMenuItem("", null, (_, _) => OpenLibrary(settings: true));
        _logsItem = new ToolStripMenuItem("", null, (_, _) => OpenFolder(_paths.LogsDir));
        _exitItem = new ToolStripMenuItem("", null, (_, _) => Exit());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_saveItem);
        menu.Items.Add(_libraryItem);
        menu.Items.Add(_clipsFolderItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_logsItem);
        menu.Items.Add(_exitItem);

        _icon = new NotifyIcon { Icon = TrayIcons.Waiting, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => OpenLibrary();
        _icon.BalloonTipClicked += (_, _) => OpenLibrary(_lastClip);

        _saver = new ClipSaver(paths, () => _config, Log.ForContext<ClipSaver>());
        // Two GetForegroundWindow calls a second, nothing more: the version info is cached per process.
        _foregroundTimer.Tick += (_, _) => CheckForeground();
        _foregroundTimer.Start();

        // First update check two minutes after start, then every six hours.
        _updateItem.Click += (_, _) => RestartToUpdate();
        _updates.UpdateReady += version => _ui.Post(_ => OnUpdateReady(version), null);
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = 6 * 60 * 60 * 1000;
            _ = Task.Run(_updates.CheckAsync);
        };
        if (_updates.IsInstalled) _updateTimer.Start();
        ApplyAutostart();
        _worker = new WorkerSupervisor(Log.ForContext<WorkerSupervisor>());
        _worker.StatusChanged += s => _ui.Post(_ => OnStatus(s), null);
        _worker.ErrorReported += (message, fatal) => _ui.Post(_ => OnWorkerError(message, fatal), null);
        _hotkey.Pressed += SaveClip;
        // An exit request set before this instance existed (by the installer's hook,
        // while some other process kept the event alive) is not meant for it.
        _exitRequest.Reset();
        _saveRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _saveRequest, (_, _) => _ui.Post(_ => SaveClip(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        _exitRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _exitRequest, (_, _) => _ui.Post(_ => Exit(), null), null, Timeout.Infinite, executeOnlyOnce: true);

        RegisterHotkey();
        if (configProblem is not null) Notify(L("Settings reset", "Configurações redefinidas"), configProblem, ToolTipIcon.Warning);
        _worker.Start(_config);
        PublishStatus(_status);
        _ = RecoverPendingAsync();

        // A save is a write to config.json.tmp and a rename, so it arrives as a burst of events.
        _configReload.Tick += (_, _) => ReloadConfig();
        _configWatcher = new FileSystemWatcher(_paths.DataDir, Path.GetFileName(_paths.ConfigFile))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _configWatcher.Changed += (_, _) => _ui.Post(_ => QueueReload(), null);
        _configWatcher.Created += (_, _) => _ui.Post(_ => QueueReload(), null);
        _configWatcher.Renamed += (_, _) => _ui.Post(_ => QueueReload(), null);
    }

    private void QueueReload()
    {
        if (_shuttingDown) return;
        _configRetries = 0;
        _configReload.Stop();
        _configReload.Start();
    }

    private void ReloadConfig()
    {
        _configReload.Stop();
        if (_shuttingDown) return;
        if (!ConfigStore.TryRead(_paths.ConfigFile, out var config, out var error))
        {
            // Caught mid-write (the file is replaced atomically, but can be busy for a moment): try again shortly.
            if (++_configRetries <= 3)
            {
                _configReload.Start();
                return;
            }
            if (error != _configError)
            {
                _configError = error;
                _log.Warning("config.json can't be read; keeping the current settings: {Error}", error);
                Notify(L("Settings not applied", "Configurações não aplicadas"),
                    L($"config.json can't be read ({error}). Ghostclip keeps the current settings.",
                        $"Não foi possível ler o config.json ({error}). O Ghostclip mantém as configurações atuais."), ToolTipIcon.Warning);
            }
            return;
        }
        _configError = null;
        var fresh = config!;
        if (fresh == _config) return;
        ApplyConfig(fresh);
    }

    // Only when the badge would change: a recording session writes a handful of times, not every second.
    private void PublishStatus(WorkerStatus status)
    {
        var published = RecorderStatus.From(Environment.ProcessId, status, _config.Fps);
        if (published == _published) return;
        try
        {
            StatusFile.Write(_paths.StatusFile, published);
            _published = published;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Debug("Could not write the status file: {Error}", ex.Message);
        }
    }

    // A new hotkey another program already holds must not leave you without one: the old one stays.
    private void RegisterHotkey(string? previous = null)
    {
        if (_hotkey.TryRegister(_config.Hotkey, out var error))
        {
            _hotkeyLabel = KeyLabels.Describe(_config.Hotkey);
        }
        else if (previous is not null && _hotkey.TryRegister(previous, out _))
        {
            _hotkeyLabel = KeyLabels.Describe(previous);
            Notify(L("Hotkey not changed", "Hotkey não alterada"),
                L($"{error} Still using {_hotkeyLabel}. Pick another one in Settings.",
                    $"{error} Continua valendo {_hotkeyLabel}. Escolha outra em Configurações."), ToolTipIcon.Warning);
        }
        else
        {
            _hotkeyLabel = null;
            Notify(L("Hotkey not available", "Hotkey indisponível"),
                L($"{error} Pick another one in Settings.", $"{error} Escolha outra em Configurações."), ToolTipIcon.Warning);
        }
        ApplyTexts();
    }

    // Every text the tray shows; again whenever the language changes.
    private void ApplyTexts()
    {
        _saveItem.Text = _hotkeyLabel is null ? L("Save clip", "Salvar clipe") : L($"Save clip ({_hotkeyLabel})", $"Salvar clipe ({_hotkeyLabel})");
        _updateItem.Text = L($"Restart to update to {_updateVersion}", $"Reiniciar para atualizar para {_updateVersion}");
        _libraryItem.Text = L("Open library", "Abrir biblioteca");
        _clipsFolderItem.Text = L("Open clips folder", "Abrir pasta de clipes");
        _settingsItem.Text = L("Settings...", "Configurações...");
        _logsItem.Text = L("Open logs folder", "Abrir pasta de logs");
        _exitItem.Text = L("Exit", "Sair");
        UpdateIconText();
        UpdateStatusItem();
    }

    // Game-only audio follows the game, not whatever is in front: alt-tabbing to
    // Discord or a browser must not start recording their audio. A program counts
    // as the game when it runs fullscreen or borderless, or has a profile.
    private void ApplyAutostart()
    {
        if (!_updates.IsInstalled) return;
        try
        {
            Autostart.Apply(_config.StartWithWindows, Environment.ProcessPath!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _log.Warning("Could not update Start with Windows: {Error}", ex.Message);
        }
    }

    private void OnUpdateReady(string version)
    {
        _updateVersion = version;
        _updateItem.Visible = true;
        ApplyTexts();
        Notify(L("Update ready", "Atualização pronta"),
            L($"Ghostclip {version} is downloaded. Restart from the tray menu when you're not playing.",
                $"O Ghostclip {version} foi baixado. Reinicie pelo menu da bandeja quando não estiver jogando."), ToolTipIcon.Info);
    }

    private void RestartToUpdate()
    {
        _log.Information("Restarting to apply an update");
        Shutdown();
        _updates.ApplyAndRestart();
        ExitThread();
    }

    private void CheckForeground()
    {
        var front = ForegroundGame.Detect();
        if (front.Game != _foreground)
        {
            _foreground = front.Game;
            _log.Debug("Foreground: {Game} (pid {Pid}, fullscreen {Fullscreen})", front.Game, front.ProcessId, front.Fullscreen);
            UpdateStatusItem();
        }

        var isGame = front.ProcessId != 0 && (front.Fullscreen || _config.ProfileFor(front.Game) is not null);
        if (isGame && front.ProcessId != _gamePid)
        {
            SetGame(front.Game, front.ProcessId);
        }
        else if (_gamePid != 0 && !isGame && !ForegroundGame.IsRunning(_gamePid))
        {
            SetGame(ClipNaming.FallbackGame, 0);
        }
    }

    private void SetGame(string game, uint pid)
    {
        _game = game;
        _gamePid = pid;
        _log.Information("Current game: {Game} (pid {Pid})", game, pid);
        _worker.SetForeground(game, pid);
        UpdateStatusItem();
    }

    private void UpdateStatusItem()
    {
        var state = _status.State switch
        {
            WorkerState.Recording => L("Recording", "Gravando"),
            WorkerState.Stopped => L("Not recording", "Sem gravar"),
            WorkerState.Probing => L("Detecting encoder", "Detectando o encoder"),
            WorkerState.WaitingForDesktop => L("Paused (screen locked)", "Pausado (tela bloqueada)"),
            _ => L("Starting", "Iniciando"),
        };
        var audio = _status.AudioRoute is { } route ? L($" · audio: {route}", $" · áudio: {PortugueseRoute(route)}") : "";
        var mic = _status.Microphone ? " · mic" : "";
        var game = _gamePid != 0 ? _game : _foreground;
        _statusItem.Text = $"{state} · {game}{audio}{mic}";
    }

    // The worker names the audio route in English, for its log as well.
    private static string PortugueseRoute(string route) => route switch
    {
        "system" => "sistema",
        "microphone" => "microfone",
        "silence" => "silêncio",
        "starting" => "iniciando",
        _ when route.StartsWith("game ", StringComparison.Ordinal) => "jogo " + route["game ".Length..],
        _ => route,
    };

    private void UpdateIconText()
    {
        var s = _status;
        var text = s.State switch
        {
            WorkerState.Recording => L($"Ghostclip: recording ({s.Encoder}, {s.Fps:0} fps{(s.Audio ? ", audio" : "")})",
                $"Ghostclip: gravando ({s.Encoder}, {s.Fps:0} fps{(s.Audio ? ", áudio" : "")})"),
            WorkerState.Stopped => L("Ghostclip: not recording", "Ghostclip: sem gravar"),
            WorkerState.Probing => L("Ghostclip: detecting the encoder", "Ghostclip: detectando o encoder"),
            WorkerState.WaitingForDesktop => L("Ghostclip: paused while the screen is locked", "Ghostclip: pausado enquanto a tela está bloqueada"),
            _ => L("Ghostclip: starting", "Ghostclip: iniciando"),
        };
        // NotifyIcon.Text is capped at 127 characters.
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void SaveClip()
    {
        // Named after the game being played, even if the hotkey came from another window.
        var front = ForegroundGame.Detect();
        var game = _gamePid != 0 && !front.Fullscreen && _config.ProfileFor(front.Game) is null ? _game : front.Game;
        ClipSnapshot snapshot;
        try
        {
            snapshot = _saver.TakeSnapshot(game, recording: _status.State == WorkerState.Recording);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Warning("Save failed: {Error}", ex.Message);
            Notify(L("Clip not saved", "Clipe não salvo"), ex.Message, ToolTipIcon.Warning);
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
            Notify(L("Clip saved", "Clipe salvo"),
                L($"{snapshot.Game}: {snapshot.Seconds:0} s. Click to open it.", $"{snapshot.Game}: {snapshot.Seconds:0} s. Clique para abrir."), ToolTipIcon.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Error("Remux failed: {Error}", ex.Message);
            Notify(L("Clip not saved", "Clipe não salvo"), ex.Message, ToolTipIcon.Error);
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
        // A status posted just before shutdown arrives after the icon is gone.
        if (_shuttingDown) return;
        _status = status;
        var icon = status.State switch
        {
            WorkerState.Recording => TrayIcons.Recording,
            WorkerState.Stopped => TrayIcons.Stopped,
            _ => TrayIcons.Waiting,
        };
        if (_icon.Icon != icon) _icon.Icon = icon;
        UpdateIconText();
        UpdateStatusItem();
        PublishStatus(status);
    }

    private void OnWorkerError(string message, bool fatal)
    {
        if (fatal)
        {
            Notify(L("Ghostclip is not recording", "O Ghostclip não está gravando"), message, ToolTipIcon.Error);
            return;
        }
        // Restarts can come in bursts (monitor unplugged): one balloon a minute.
        if (DateTime.UtcNow - _lastWarningUtc < TimeSpan.FromMinutes(1)) return;
        _lastWarningUtc = DateTime.UtcNow;
        Notify(L("Recording interrupted", "Gravação interrompida"),
            L("Ghostclip is restarting the recorder. The buffer is kept.", "O Ghostclip está reiniciando o gravador. O buffer foi mantido."), ToolTipIcon.Warning);
    }

    // The worker restarts recording only when the capture settings changed (GhostclipConfig.SameCaptureAs).
    private void ApplyConfig(GhostclipConfig config)
    {
        var previousHotkey = _config.Hotkey;
        _config = config;
        _level.MinimumLevel = Program.ToSerilog(config.LogLevel);
        UiText.Use(config.Language);
        // Both redo the menu's texts, in the language just set.
        if (config.Hotkey != previousHotkey) RegisterHotkey(previousHotkey);
        else ApplyTexts();
        ApplyAutostart();
        _worker.UpdateConfig(config);
        _log.Information("Settings applied");
    }

    private void Notify(string title, string text, ToolTipIcon icon)
    {
        if (!_shuttingDown) _icon.ShowBalloonTip(4000, title, text, icon);
    }

    // The library is its own process: nothing of it stays in the tray once its window closes.
    private void OpenLibrary(string? clip = null, bool settings = false)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        info.ArgumentList.Add("--library");
        if (settings) info.ArgumentList.Add(Library.LibraryProgram.SettingsArgument);
        else if (clip is not null) info.ArgumentList.Add(clip);
        try
        {
            using var _ = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Warning("Could not open the library: {Error}", ex.Message);
        }
    }

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Exit()
    {
        Shutdown();
        ExitThread();
    }

    private void Shutdown()
    {
        _shuttingDown = true;
        _icon.Visible = false;
        _hotkey.Dispose();
        _foregroundTimer.Dispose();
        _updateTimer.Dispose();
        _saveRequestWait.Unregister(null);
        _saveRequest.Dispose();
        _exitRequestWait.Unregister(null);
        _exitRequest.Dispose();
        _configWatcher.Dispose();
        _configReload.Dispose();
        StatusFile.Delete(_paths.StatusFile);
        // Give the worker a moment to close its last segment cleanly.
        Task.Run(async () => await _worker.DisposeAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(8));
        _sound.Dispose();
        _icon.Dispose();
    }
}
