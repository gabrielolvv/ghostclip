using System.Diagnostics;
using Ghostclip.Core;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ghostclip.Host;

/// <summary>The tray app: icon and menu, hotkeys, saving, screenshots, sessions, and the worker's lifecycle.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly GhostclipPaths _paths;
    private readonly LoggingLevelSwitch _level;
    private readonly ILogger _log = Log.ForContext<TrayApp>();
    private readonly SynchronizationContext _ui;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _saveItem;
    private readonly ToolStripMenuItem _shortClipItem;
    private readonly ToolStripMenuItem _screenshotItem;
    private readonly ToolStripMenuItem _sessionItem;
    private readonly ToolStripMenuItem _micItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _updateItem = new() { Visible = false };
    private readonly ToolStripMenuItem _libraryItem;
    private readonly ToolStripMenuItem _clipsFolderItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _logsItem;
    private readonly ToolStripMenuItem _exitItem;
    // Each registered hotkey as the menu names it (missing when none is), and the update waiting to be applied.
    private readonly Dictionary<HotkeyAction, string> _hotkeyLabels = [];
    private string? _updateVersion;
    private readonly ScreenshotTaker _screenshots;
    private readonly SessionRecorder _session;
    private readonly StorageKeeper _storage;
    private readonly UpdateService _updates = new(Log.ForContext<UpdateService>());
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 2 * 60 * 1000 };
    private readonly HotkeyWindow _hotkey = new();
    private readonly WorkerSupervisor _worker;
    private readonly ClipSaver _saver;
    private readonly System.Windows.Forms.Timer _foregroundTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _diskTimer = new() { Interval = 60_000 };
    private readonly LowDiskWatch _lowDisk = new();
    private string _foreground = ClipNaming.FallbackGame;
    private string _game = ClipNaming.FallbackGame;
    private uint _gamePid;
    private readonly ConfirmationSound _sound = new();
    private readonly ConfirmationSound _mutedSound = new(784.0, 523.25);
    private readonly ConfirmationSound _unmutedSound = new(523.25, 784.0);
    // Muted by the hotkey. Never saved: Ghostclip always starts with the microphone on.
    private bool _micMuted;
    private bool _paused;
    private string? _pausedFor;
    private bool Paused => _paused || _pausedFor is not null;
    private bool _welcomePending;
    private readonly List<(EventWaitHandle Request, RegisteredWaitHandle Wait)> _commands = [];
    private readonly EventWaitHandle _exitRequest = new(false, EventResetMode.AutoReset, Program.ExitEventName);
    private readonly RegisteredWaitHandle _exitRequestWait;
    private readonly EventWaitHandle _updateRequest = new(false, EventResetMode.AutoReset, Program.UpdateEventName);
    private readonly EventWaitHandle _hotkeyCapture = new(false, EventResetMode.AutoReset, Program.HotkeyCaptureEventName);
    private readonly RegisteredWaitHandle _hotkeyCaptureWait;
    private readonly System.Windows.Forms.Timer _hotkeyResume = new() { Interval = 1000 };
    private readonly RegisteredWaitHandle _updateRequestWait;
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
    // Why the recorder stopped, for the library: the balloon that said so is gone after a few seconds.
    private string? _problem;

    public TrayApp(GhostclipPaths paths, GhostclipConfig config, string? configProblem, LoggingLevelSwitch level, bool firstRun)
    {
        _welcomePending = firstRun;
        _paths = paths;
        _config = config;
        _level = level;
        _ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_ui);

        // Texts come from ApplyTexts, in the chosen language.
        _saveItem = new ToolStripMenuItem("", null, (_, _) => SaveClip()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        _shortClipItem = new ToolStripMenuItem("", null, (_, _) => SaveClip(_config.ShortClipSeconds));
        _screenshotItem = new ToolStripMenuItem("", null, (_, _) => TakeScreenshot());
        _sessionItem = new ToolStripMenuItem("", null, (_, _) => ToggleSession());
        _micItem = new ToolStripMenuItem("", null, (_, _) => ToggleMicrophone());
        _pauseItem = new ToolStripMenuItem("", null, (_, _) => TogglePause());
        _libraryItem = new ToolStripMenuItem("", null, (_, _) => OpenLibrary());
        _clipsFolderItem = new ToolStripMenuItem("", null, (_, _) => OpenFolder(_config.ResolvedClipsFolder));
        _settingsItem = new ToolStripMenuItem("", null, (_, _) => OpenLibrary(settings: true));
        _logsItem = new ToolStripMenuItem("", null, (_, _) => OpenFolder(_paths.LogsDir));
        _exitItem = new ToolStripMenuItem("", null, (_, _) => ExitFromMenu());
        var menu = new ContextMenuStrip();
        TrayMenuStyle.Apply(menu);
        _updateItem.Name = TrayMenuStyle.UpdateItemName;
        menu.Items.Add(_statusItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_saveItem);
        menu.Items.Add(_shortClipItem);
        menu.Items.Add(_screenshotItem);
        menu.Items.Add(_sessionItem);
        menu.Items.Add(_micItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
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
        _screenshots = new ScreenshotTaker(() => _config, Log.ForContext<ScreenshotTaker>());
        _session = new SessionRecorder(paths, () => _config, _saver, Log.ForContext<SessionRecorder>());
        _storage = new StorageKeeper(paths, () => _config, Log.ForContext<StorageKeeper>());
        // The menu shows how long the session has been running.
        _session.Ticked += ApplyTexts;
        // Two GetForegroundWindow calls a second, nothing more: the version info is cached per process.
        _foregroundTimer.Tick += (_, _) => CheckForeground();
        _foregroundTimer.Interval = ForegroundInterval(_config);
        _foregroundTimer.Start();
        // Windows shutting down or signing out: a clean exit, so the buffer goes and a session is kept, as with Exit.
        Microsoft.Win32.SystemEvents.SessionEnded += OnSessionEnded;
        _diskTimer.Tick += (_, _) => CheckDiskSpace();
        _diskTimer.Start();

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
        _hotkey.Pressed += action =>
        {
            switch (action)
            {
                case HotkeyAction.SaveClip: SaveClip(); break;
                case HotkeyAction.Screenshot: TakeScreenshot(); break;
                case HotkeyAction.Session: ToggleSession(); break;
                case HotkeyAction.MicMute: ToggleMicrophone(); break;
                case HotkeyAction.ShortClip: SaveClip(_config.ShortClipSeconds); break;
                case HotkeyAction.Pause: TogglePause(); break;
            }
        };
        // An exit request set before this instance existed (by the installer's hook,
        // while some other process kept the event alive) is not meant for it.
        _exitRequest.Reset();
        foreach (var command in Enum.GetValues<TrayCommand>())
        {
            var request = new EventWaitHandle(false, EventResetMode.AutoReset, TrayCommands.EventName(command));
            _commands.Add((request, ThreadPool.RegisterWaitForSingleObject(
                request, (_, _) => _ui.Post(_ => RunCommand(command), null), null, Timeout.Infinite, executeOnlyOnce: false)));
        }
        _exitRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _exitRequest, (_, _) => _ui.Post(_ => Exit(), null), null, Timeout.Infinite, executeOnlyOnce: true);
        // The library's "Update now" button, the same as "Restart to update" in this menu.
        _updateRequest.Reset();
        _updateRequestWait = ThreadPool.RegisterWaitForSingleObject(
            _updateRequest, (_, _) => _ui.Post(_ => { if (_updateVersion is not null && !_shuttingDown) RestartToUpdate(); }, null),
            null, Timeout.Infinite, executeOnlyOnce: false);
        _hotkeyCaptureWait = ThreadPool.RegisterWaitForSingleObject(
            _hotkeyCapture, (_, _) => _ui.Post(_ => OnHotkeyCaptureChanged(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        _hotkeyResume.Tick += (_, _) => ResumeHotkeys();

        RememberBufferFolder();
        ClearOldBuffers();
        RegisterHotkeys();
        if (configProblem is not null) Notify(L("Settings reset", "Configurações redefinidas", "Configuración restablecida"), configProblem, ToolTipIcon.Warning);
        // A listed program already in front: the worker hears it before anything it could record.
        CheckPrivateAppNow();
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
                Notify(L("Settings not applied", "Configurações não aplicadas", "Configuración no aplicada"),
                    L($"config.json can't be read ({error}). Ghostclip keeps the current settings.",
                        $"Não foi possível ler o config.json ({error}). O Ghostclip mantém as configurações atuais.", $"No se puede leer config.json ({error}). Ghostclip mantiene la configuración actual."), ToolTipIcon.Warning);
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
        var published = RecorderStatus.From(Environment.ProcessId, status, _config.Fps, _problem, _updateVersion);
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
    // All are let go first, so two actions can swap keys.
    private void RegisterHotkeys(GhostclipConfig? previous = null)
    {
        _hotkey.UnregisterAll();
        _hotkeyLabels.Clear();
        // A save lands in the second before the keys come back: they register now, so one already taken keeps its old key.
        if (!Library.LibraryProgram.IsCapturingHotkey()) _hotkey.Resume();
        foreach (var action in Enum.GetValues<HotkeyAction>()) Register(action, previous);
        ApplyTexts();
    }

    // Turning the microphone on or off takes or frees the mute key alone: the others stay, with no new warnings.
    private void RegisterMuteHotkey()
    {
        _hotkey.Unregister(HotkeyAction.MicMute);
        _hotkeyLabels.Remove(HotkeyAction.MicMute);
        Register(HotkeyAction.MicMute, previous: null);
        ApplyTexts();
    }

    // From another launch of Ghostclip.exe: the same as the hotkeys, whether or not they have a key.
    private void RunCommand(TrayCommand command)
    {
        if (_shuttingDown) return;
        switch (command)
        {
            case TrayCommand.SaveClip: SaveClip(); break;
            case TrayCommand.ShortClip: SaveClip(_config.ShortClipSeconds); break;
            case TrayCommand.Screenshot: TakeScreenshot(); break;
            case TrayCommand.Session: ToggleSession(); break;
            case TrayCommand.MicMute: ToggleMicrophone(); break;
            case TrayCommand.Pause: TogglePause(); break;
        }
    }

    // While the settings page waits for a new hotkey, a key this app holds would run its action instead of reaching
    // the page. They come back a second after it stops waiting, so a key still down can't run the action it just
    // chose; the same timer notices a library that died while waiting.
    private void OnHotkeyCaptureChanged()
    {
        if (_shuttingDown) return;
        if (Library.LibraryProgram.IsCapturingHotkey()) _hotkey.Pause();
        _hotkeyResume.Stop();
        _hotkeyResume.Start();
    }

    private void ResumeHotkeys()
    {
        if (_shuttingDown || Library.LibraryProgram.IsCapturingHotkey()) return;
        _hotkeyResume.Stop();
        var lost = _hotkey.Resume();
        if (lost.Count == 0) return;
        foreach (var action in lost)
        {
            _hotkeyLabels.Remove(action);
            Register(action, previous: null);
        }
        ApplyTexts();
    }

    private void Register(HotkeyAction action, GhostclipConfig? previous)
    {
        // Without a microphone track the mute key stays free for other programs.
        if (action == HotkeyAction.MicMute && !_config.Microphone) return;
        // So do the short clip and pause keys, until one is chosen.
        if (action == HotkeyAction.ShortClip && !_config.HasShortClipHotkey) return;
        if (action == HotkeyAction.Pause && !_config.HasPauseHotkey) return;
        var gesture = _config.Hotkeys[(int)action];
        var old = previous?.Hotkeys[(int)action];
        if (_hotkey.TryRegister(action, gesture, out var error))
        {
            _hotkeyLabels[action] = KeyLabels.Describe(gesture);
        }
        else if (old is not null && old != gesture && _hotkey.TryRegister(action, old, out _))
        {
            _hotkeyLabels[action] = KeyLabels.Describe(old);
            Notify(L("Hotkey not changed", "Hotkey não alterada", "Tecla no cambiada"),
                L($"{error} Still using {_hotkeyLabels[action]}. Pick another one in Settings.",
                    $"{error} Continua valendo {_hotkeyLabels[action]}. Escolha outra em Configurações.", $"{error} Sigue vigente {_hotkeyLabels[action]}. Elige otra en Configuración."), ToolTipIcon.Warning);
        }
        else
        {
            Notify(L("Hotkey not available", "Hotkey indisponível", "Tecla no disponible"),
                L($"{error} Pick another one in Settings.", $"{error} Escolha outra em Configurações.", $"{error} Elige otra en Configuración."), ToolTipIcon.Warning);
        }
    }

    // "Save clip (Alt+F10)", or without the key when none is registered.
    private string WithHotkey(HotkeyAction action, string text) =>
        _hotkeyLabels.TryGetValue(action, out var label) ? $"{text} ({label})" : text;

    // Every text the tray shows; again whenever the language changes, and every second of a session.
    private void ApplyTexts()
    {
        _saveItem.Text = WithHotkey(HotkeyAction.SaveClip, L("Save clip", "Salvar clipe", "Guardar clip"));
        _shortClipItem.Text = WithHotkey(HotkeyAction.ShortClip, L($"Save last {_config.ShortClipSeconds} s", $"Salvar últimos {_config.ShortClipSeconds} s", $"Guardar últimos {_config.ShortClipSeconds} s"));
        _shortClipItem.Visible = _config.HasShortClipHotkey;
        _screenshotItem.Text = WithHotkey(HotkeyAction.Screenshot, L("Take screenshot", "Tirar print", "Hacer captura"));
        _sessionItem.Text = _session.Running
            ? WithHotkey(HotkeyAction.Session, L($"Stop session recording · {Clock(_session.Elapsed)}", $"Parar gravação da sessão · {Clock(_session.Elapsed)}", $"Detener grabación de la sesión · {Clock(_session.Elapsed)}"))
            : WithHotkey(HotkeyAction.Session, L("Record session", "Gravar sessão", "Grabar sesión"));
        _micItem.Text = _micMuted
            ? WithHotkey(HotkeyAction.MicMute, L("Unmute microphone", "Desmutar microfone", "Activar micrófono"))
            : WithHotkey(HotkeyAction.MicMute, L("Mute microphone", "Mutar microfone", "Silenciar micrófono"));
        _micItem.Enabled = _config.Microphone;
        _pauseItem.Text = _paused
            ? WithHotkey(HotkeyAction.Pause, L("Resume recording", "Retomar gravação", "Reanudar grabación"))
            : WithHotkey(HotkeyAction.Pause, L("Pause recording", "Pausar gravação", "Pausar grabación"));
        _updateItem.Text = L($"Restart to update to {_updateVersion}", $"Reiniciar para atualizar para {_updateVersion}", $"Reiniciar para actualizar a {_updateVersion}");
        _libraryItem.Text = L("Open library", "Abrir biblioteca", "Abrir biblioteca");
        _clipsFolderItem.Text = L("Open clips folder", "Abrir pasta de clipes", "Abrir carpeta de clips");
        _settingsItem.Text = L("Settings...", "Configurações...", "Configuración...");
        _logsItem.Text = L("Open logs folder", "Abrir pasta de logs", "Abrir carpeta de registros");
        _exitItem.Text = L("Exit", "Sair", "Salir");
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
        // The library offers it too, with a button.
        PublishStatus(_status);
        Notify(L("Update ready", "Atualização pronta", "Actualización lista"),
            L($"Ghostclip {version} installs when you exit Ghostclip or the next time it starts. To install it now, use the library or the tray menu.",
                $"O Ghostclip {version} será instalado quando você sair do Ghostclip ou na próxima vez que ele abrir. Para instalar agora, use a biblioteca ou o menu da bandeja.", $"Ghostclip {version} se instala cuando salgas de Ghostclip o la próxima vez que se abra. Para instalarlo ahora, usa la biblioteca o el menú de la bandeja."), ToolTipIcon.Info);
    }

    private void RestartToUpdate()
    {
        _log.Information("Restarting to apply an update");
        Shutdown();
        _updates.ApplyAndRestart();
        ExitThread();
    }

    // Quicker with programs to pause for: what they show should reach the disk for as short a time as possible.
    private static int ForegroundInterval(GhostclipConfig config) => config.PrivateApps.Count > 0 ? 500 : 2000;

    private void CheckForeground()
    {
        var front = ForegroundGame.Detect();
        var privateApp = _config.PausingApp(_pausedFor, front.Process, front.Game);
        if (privateApp != _pausedFor)
        {
            var before = Paused;
            _pausedFor = privateApp;
            PauseChanged(before, fromKey: false);
        }
        if (front.Game != _foreground)
        {
            _foreground = front.Game;
            _log.Debug("Foreground: {Game} (pid {Pid}, fullscreen {Fullscreen})", front.Game, front.ProcessId, front.Fullscreen);
            UpdateStatusItem();
        }

        var isGame = privateApp is null && front.ProcessId != 0 && (front.Fullscreen || _config.ProfileFor(front.Game) is not null);
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
            WorkerState.Recording => L("Recording", "Gravando", "Grabando"),
            WorkerState.Stopped => L("Not recording", "Sem gravar", "Sin grabar"),
            WorkerState.Probing => L("Detecting encoder", "Detectando o encoder", "Detectando el encoder"),
            WorkerState.WaitingForDesktop => L("Paused (screen locked)", "Pausado (tela bloqueada)", "Pausado (pantalla bloqueada)"),
            WorkerState.Paused => L("Paused", "Pausado", "Pausado"),
            _ => L("Starting", "Iniciando", "Iniciando"),
        };
        var audio = _status.AudioRoute is { } route ? L($" · audio: {route}", $" · áudio: {PortugueseRoute(route)}", $" · audio: {SpanishRoute(route)}") : "";
        // What the recorder does, not what the tray asked: a mute that didn't reach it shows as the live mic.
        var mic = (_status.MicrophoneMuted ? L(" · mic muted", " · mic mudo", " · mic silenciado") : _status.Microphone ? " · mic" : "") + (_status.Discord ? " · discord" : "");
        var game = _gamePid != 0 ? _game : _foreground;
        var session = _session.Running ? L($"● Session {Clock(_session.Elapsed)} · ", $"● Sessão {Clock(_session.Elapsed)} · ", $"● Sesión {Clock(_session.Elapsed)} · ") : "";
        _statusItem.Text = $"{session}{state} · {game}{audio}{mic}";
    }

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";

    // The worker names the audio route in English, for its log as well.
    private static string SpanishRoute(string route) => route switch
    {
        "microphone" => "micrófono",
        "silence" => "silencio",
        "system without discord" => "sistema sin discord",
        "starting" => "iniciando",
        _ when route.StartsWith("game ", StringComparison.Ordinal) => "juego " + route["game ".Length..],
        _ => route == "system" ? "sistema" : route,
    };

    private static string PortugueseRoute(string route) => route switch
    {
        "system" => "sistema",
        "microphone" => "microfone",
        "silence" => "silêncio",
        "system without discord" => "sistema sem discord",
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
                $"Ghostclip: gravando ({s.Encoder}, {s.Fps:0} fps{(s.Audio ? ", áudio" : "")})", $"Ghostclip: grabando ({s.Encoder}, {s.Fps:0} fps{(s.Audio ? ", audio" : "")})"),
            WorkerState.Stopped => L("Ghostclip: not recording", "Ghostclip: sem gravar", "Ghostclip: sin grabar"),
            WorkerState.Probing => L("Ghostclip: detecting the encoder", "Ghostclip: detectando o encoder", "Ghostclip: detectando el encoder"),
            WorkerState.WaitingForDesktop => L("Ghostclip: paused while the screen is locked", "Ghostclip: pausado enquanto a tela está bloqueada", "Ghostclip: en pausa mientras la pantalla está bloqueada"),
            WorkerState.Paused => L("Ghostclip: paused, not recording", "Ghostclip: pausado, sem gravar", "Ghostclip: en pausa, sin grabar"),
            _ => L("Ghostclip: starting", "Ghostclip: iniciando", "Ghostclip: iniciando"),
        };
        // NotifyIcon.Text is capped at 127 characters.
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    // Named after the game being played, even if the hotkey came from another window.
    private string CurrentGame()
    {
        var front = ForegroundGame.Detect();
        return _gamePid != 0 && !front.Fullscreen && _config.ProfileFor(front.Game) is null ? _game : front.Game;
    }

    // Without a length, the one set for the game.
    private async void SaveClip(int? seconds = null)
    {
        CheckPrivateAppNow();
        if (Paused)
        {
            Notify(L("Clip not saved", "Clipe não salvo", "Clip no guardado"), L("Recording is paused.", "A gravação está pausada.", "La grabación está en pausa."), ToolTipIcon.Warning);
            return;
        }
        var game = CurrentGame();
        // During a session the moment is also marked in it; the clip is saved as well.
        if (_session.Running) _session.AddBookmark();
        ClipSnapshot snapshot;
        try
        {
            // Milliseconds with hard links. A drive without them copies, which mustn't hold up the tray and its other keys.
            var recording = _status.State == WorkerState.Recording;
            snapshot = await Task.Run(() => _saver.TakeSnapshot(game, recording, seconds)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Warning("Save failed: {Error}", ex.Message);
            Notify(L("Clip not saved", "Clipe não salvo", "Clip no guardado"), ex.Message, ToolTipIcon.Warning);
            return;
        }
        // The clip is safe from here on: confirm right away, the MP4 follows.
        if (_config.ConfirmationSound) _sound.Play();
        _ = FinishAsync(snapshot);
    }

    // Nothing on screen reaches the disk until it is pressed again, for a bank's page or a password. The buffer goes,
    // so there is nothing to save from before it either. Not kept across restarts: Ghostclip always starts recording.
    private void TogglePause()
    {
        var before = Paused;
        _paused = !_paused;
        PauseChanged(before, fromKey: true);
    }

    // A key pressed the moment a listed program comes to the front must not save what it shows.
    private void CheckPrivateAppNow()
    {
        if (_config.PrivateApps.Count > 0) CheckForeground();
    }

    // The key or menu, or a program from Settings > Clips in front: paused while either says so.
    private void PauseChanged(bool before, bool fromKey)
    {
        ApplyTexts();
        if (Paused == before)
        {
            if (fromKey && _pausedFor is { } holding)
            {
                Notify(_paused ? L("Recording stays paused", "A gravação continua pausada", "La grabación sigue en pausa") : L("Recording still paused", "Gravação continua pausada", "La grabación sigue en pausa"),
                    _paused
                        ? L($"Also after {holding} leaves the front, until you press it again.", $"Também depois que {holding} sair da frente, até você apertar de novo.", $"También después de que {holding} deje el primer plano, hasta que la pulses de nuevo.")
                        : L($"{holding} is in front.", $"{holding} está na frente.", $"{holding} está en primer plano."), ToolTipIcon.None);
            }
            return;
        }
        // A session is the buffer kept, and the buffer is about to go: saved as it is. For a listed program it goes on
        // in a new part afterwards, since a moment in a password manager shouldn't end it (see OnStatus).
        if (Paused && _pausedFor is null && _session.Running) StopSession();
        _worker.SetPaused(Paused);
        _log.Information(Paused ? "Recording paused ({App})" : "Recording resumed", _pausedFor ?? "from the tray");
        // A balloon doesn't show over a fullscreen game: the tone says it there, even with the other sounds off.
        (Paused ? _mutedSound : _unmutedSound).Play();
        if (Paused)
        {
            Notify(L("Recording paused", "Gravação pausada", "Grabación en pausa"),
                (_pausedFor is { } app ? L($"{app} is in front. ", $"{app} está na frente. ", $"{app} está en primer plano. ") : "") +
                L("The replay buffer is deleted, and nothing is recorded until you resume.", "O buffer de replay é apagado, e nada é gravado até você retomar.", "El búfer de repetición se borra y no se graba nada hasta que reanudes."), ToolTipIcon.None);
        }
        else
        {
            Notify(L("Recording resumed", "Gravação retomada", "Grabación reanudada"), L("Clips start from now.", "Os clipes começam a partir de agora.", "Los clips empiezan desde ahora."), ToolTipIcon.None);
        }
    }

    // The microphone track records silence until the key is pressed again. Recording goes on: nothing restarts.
    private void ToggleMicrophone()
    {
        if (!_config.Microphone) return;
        _micMuted = !_micMuted;
        _worker.SetMicrophoneMuted(_micMuted);
        _log.Information(_micMuted ? "Microphone muted by the hotkey" : "Microphone unmuted by the hotkey");
        // A balloon doesn't show over a fullscreen game: the tone says it there, even with the other sounds off,
        // since nothing else would tell you whether the microphone records.
        (_micMuted ? _mutedSound : _unmutedSound).Play();
        if (_micMuted)
        {
            Notify(L("Microphone muted", "Microfone mudo", "Micrófono silenciado"),
                L("Clips record silence on the microphone track until you press it again.", "Os clipes gravam silêncio na faixa do microfone até você apertar de novo.", "Los clips graban silencio en la pista del micrófono hasta que lo pulses de nuevo."), ToolTipIcon.None);
        }
        else
        {
            var game = CurrentGame();
            if (_config.MicrophoneMutedFor(game, byHotkey: false))
            {
                Notify(L("Microphone still muted", "Microfone continua mudo", "El micrófono sigue silenciado"),
                    L($"{game}'s profile mutes it.", $"O perfil de {game} deixa o microfone mudo.", $"El perfil de {game} lo silencia."), ToolTipIcon.None);
            }
            else
            {
                Notify(L("Microphone unmuted", "Microfone desmutado", "Micrófono activado"),
                    L("The microphone track records again.", "A faixa do microfone volta a gravar.", "La pista del micrófono vuelve a grabar."), ToolTipIcon.None);
            }
        }
        ApplyTexts();
    }

    private async void TakeScreenshot()
    {
        CheckPrivateAppNow();
        if (Paused)
        {
            Notify(L("Screenshot not saved", "Print não salvo", "Captura no guardada"), L("Recording is paused.", "A gravação está pausada.", "La grabación está en pausa."), ToolTipIcon.Warning);
            return;
        }
        var game = CurrentGame();
        try
        {
            var path = await _screenshots.TakeAsync(game).ConfigureAwait(true);
            if (_config.ConfirmationSound) _sound.Play();
            _lastClip = path;
            _storage.Enforce();
            Notify(L("Screenshot saved", "Print salvo", "Captura guardada"), L($"{game}. Click to open it.", $"{game}. Clique para abrir.", $"{game}. Haz clic para abrirla."), ToolTipIcon.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Warning("Screenshot failed: {Error}", ex.Message);
            Notify(L("Screenshot not saved", "Print não salvo", "Captura no guardada"), ex.Message, ToolTipIcon.Warning);
        }
    }

    private void ToggleSession()
    {
        if (_session.Running)
        {
            StopSession();
            return;
        }
        // A session is the buffer kept: there must be a buffer being written.
        if (Paused || _status.State != WorkerState.Recording)
        {
            Notify(L("Session not started", "Sessão não iniciada", "Sesión no iniciada"),
                L("Ghostclip isn't recording right now.", "O Ghostclip não está gravando agora.", "Ghostclip no está grabando ahora."), ToolTipIcon.Warning);
            return;
        }
        _session.Start(CurrentGame());
        if (_config.ConfirmationSound) _sound.Play();
        var stop = _hotkeyLabels.TryGetValue(HotkeyAction.Session, out var s) ? s : L("the tray menu", "o menu da bandeja", "el menú de la bandeja");
        var mark = _hotkeyLabels.TryGetValue(HotkeyAction.SaveClip, out var c) ? c : L("the clip hotkey", "a hotkey de clipe", "la tecla de clip");
        Notify(L("Recording the session", "Gravando a sessão", "Grabando la sesión"),
            L($"{_session.Game}. {mark} saves a clip and adds a bookmark; {stop} stops.",
                $"{_session.Game}. {mark} salva um clipe e marca o momento; {stop} para.", $"{_session.Game}. {mark} guarda un clip y añade un marcador; {stop} detiene."), ToolTipIcon.None);
        ApplyTexts();
    }

    private void StopSession()
    {
        var earlierParts = _session.SavedParts;
        var snapshot = _session.Stop();
        ApplyTexts();
        if (snapshot is null)
        {
            // Stopped while the recorder was down: the parts before it are saved already.
            if (earlierParts == 0)
            {
                Notify(L("Session not saved", "Sessão não salva", "Sesión no guardada"), L("Nothing was recorded.", "Nada foi gravado.", "No se grabó nada."), ToolTipIcon.Warning);
            }
            return;
        }
        if (_config.ConfirmationSound) _sound.Play();
        _ = FinishAsync(snapshot, session: true);
    }

    private async Task FinishAsync(ClipSnapshot snapshot, bool session = false)
    {
        try
        {
            var path = await Task.Run(() => _saver.RemuxAsync(snapshot)).ConfigureAwait(true);
            _lastClip = path;
            // The oldest make room for it; the newest is always kept.
            _storage.Enforce();
            if (session)
            {
                Notify(L("Session saved", "Sessão salva", "Sesión guardada"),
                    L($"{snapshot.Game}: {Clock(TimeSpan.FromSeconds(snapshot.Seconds))}, {snapshot.Bookmarks?.Count ?? 0} bookmarks. Click to open it.",
                        $"{snapshot.Game}: {Clock(TimeSpan.FromSeconds(snapshot.Seconds))}, {snapshot.Bookmarks?.Count ?? 0} marcadores. Clique para abrir.", $"{snapshot.Game}: {Clock(TimeSpan.FromSeconds(snapshot.Seconds))}, {snapshot.Bookmarks?.Count ?? 0} marcadores. Haz clic para abrirla."), ToolTipIcon.None);
                return;
            }
            Notify(L("Clip saved", "Clipe salvo", "Clip guardado"),
                L($"{snapshot.Game}: {snapshot.Seconds:0} s. Click to open it.", $"{snapshot.Game}: {snapshot.Seconds:0} s. Clique para abrir.", $"{snapshot.Game}: {snapshot.Seconds:0} s. Haz clic para abrirlo."), ToolTipIcon.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _log.Error("Remux failed: {Error}", ex.Message);
            Notify(L("Clip not saved", "Clipe não salvo", "Clip no guardado"), ex.Message, ToolTipIcon.Error);
        }
    }

    // Snapshots a previous run took but never turned into MP4s (crash, shutdown).
    private async Task RecoverPendingAsync()
    {
        foreach (var snapshot in _saver.PendingFromEarlierRuns())
        {
            _log.Information("Finishing a clip from an earlier run: {Dir}", snapshot.Dir);
            await FinishAsync(snapshot, session: snapshot.Dir.EndsWith("-session", StringComparison.Ordinal)).ConfigureAwait(true);
        }
        _storage.Enforce();
    }

    private void OnStatus(WorkerStatus status)
    {
        // A status posted just before shutdown arrives after the icon is gone.
        if (_shuttingDown) return;
        var previous = _status;
        _status = status;
        // A session can't join segments across a recorder restart: the part so far is saved, and the
        // session goes on in a new file once recording is back. A restart too quick for a status in
        // between still shows as a new run.
        var wasRecording = previous.State == WorkerState.Recording;
        var recording = status.State == WorkerState.Recording;
        var newRun = wasRecording && recording && previous.RunId is { } before && status.RunId is { } now && before != now;
        if (_session.Running && (newRun || (wasRecording && !recording))
            && _session.RecorderStopped(newRun ? status.RunStart : null) is { } part)
        {
            _ = FinishAsync(part, session: true);
        }
        if (recording && _welcomePending) Welcome();
        if (_session.Running && recording && (newRun || !wasRecording))
        {
            _session.RecorderResumed(status.RunId is null ? null : status.RunStart);
        }
        // Probing again (a settings change) or recording: whatever stopped it is over.
        if (status.State != WorkerState.Stopped) _problem = null;
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

    // After a new install all there is to see is an icon in the tray. Said once recording has really started: if it
    // can't, the message about that is the one to read.
    private void Welcome()
    {
        _welcomePending = false;
        var key = _hotkeyLabels.TryGetValue(HotkeyAction.SaveClip, out var label) ? label : L("the tray menu", "o menu da bandeja", "el menú de la bandeja");
        Notify(L("Ghostclip is recording", "O Ghostclip está gravando", "Ghostclip está grabando"),
            L($"Press {key} to save the last {_config.ClipSeconds} s. Double-click this icon for your clips and settings.",
                $"Aperte {key} para salvar os últimos {_config.ClipSeconds} s. Dê dois cliques neste ícone para ver os clipes e as configurações.", $"Pulsa {key} para guardar los últimos {_config.ClipSeconds} s. Haz doble clic en este ícono para ver tus clips y la configuración."), ToolTipIcon.None);
    }

    // Saving says so when a clip doesn't fit, but by then the moment has happened: this says it before.
    private void CheckDiskSpace()
    {
        var threshold = LowDiskWatch.Threshold(_config);
        foreach (var folder in new[] { _paths.BufferDirFor(_config), _config.ResolvedClipsFolder })
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(folder));
                if (string.IsNullOrEmpty(root)) continue;
                var drive = new DriveInfo(root);
                if (!drive.IsReady || !_lowDisk.ShouldWarn(root, drive.AvailableFreeSpace, threshold)) continue;
                var gb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                _log.Warning("Drive {Drive} has {Gb:0.0} GB free", root, gb);
                Notify(L($"Drive {root} is almost full", $"O disco {root} está quase cheio", $"El disco {root} está casi lleno"),
                    L($"{gb:0.0} GB free. When it fills up, Ghostclip can't record or save clips. A space limit in Settings can make room by itself.",
                        $"{gb:0.0} GB livres. Quando encher, o Ghostclip não consegue gravar nem salvar clipes. Um limite de espaço em Configurações pode abrir espaço sozinho.", $"{gb:0.0} GB libres. Cuando se llene, Ghostclip no podrá grabar ni guardar clips. Un límite de espacio en Configuración puede liberar espacio solo."), ToolTipIcon.Warning);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // A network folder has no drive to ask, and one unplugged says so elsewhere.
                _log.Debug("Could not check the free space for {Folder}: {Error}", folder, ex.Message);
            }
        }
    }

    private void OnWorkerError(string message, bool fatal)
    {
        if (fatal)
        {
            _problem = message;
            PublishStatus(_status);
            Notify(L("Ghostclip is not recording", "O Ghostclip não está gravando", "Ghostclip no está grabando"), message, ToolTipIcon.Error);
            return;
        }
        // Restarts can come in bursts (monitor unplugged): one balloon a minute.
        if (DateTime.UtcNow - _lastWarningUtc < TimeSpan.FromMinutes(1)) return;
        _lastWarningUtc = DateTime.UtcNow;
        Notify(L("Recording interrupted", "Gravação interrompida", "Grabación interrumpida"),
            L("Ghostclip is restarting the recorder. The buffer is kept.", "O Ghostclip está reiniciando o gravador. O buffer foi mantido.", "Ghostclip está reiniciando el grabador. El búfer se conservó."), ToolTipIcon.Warning);
    }

    // The worker restarts recording only when the capture settings changed (GhostclipConfig.SameCaptureAs).
    private void ApplyConfig(GhostclipConfig config)
    {
        var previous = _config;
        // The recorder is about to start over in another format, and a session's segments must all join: save it as it is.
        if (_session.Running && !config.SameCaptureAs(previous))
        {
            _log.Information("Capture settings changed; ending the session");
            StopSession();
        }
        _config = config;
        _foregroundTimer.Interval = ForegroundInterval(config);
        RememberBufferFolder();
        _level.MinimumLevel = Program.ToSerilog(config.LogLevel);
        UiText.Use(config.Language);
        // Turning the microphone off lets the mute key go, and on again starts unmuted.
        if (!config.Microphone && _micMuted)
        {
            _micMuted = false;
            _worker.SetMicrophoneMuted(false);
        }
        // Each redoes the menu's texts, in the language just set.
        if (!config.Hotkeys.SequenceEqual(previous.Hotkeys)) RegisterHotkeys(previous);
        else if (config.Microphone != previous.Microphone) RegisterMuteHotkey();
        else ApplyTexts();
        ApplyAutostart();
        _worker.UpdateConfig(config);
        // A new or lower limit, or a different clips folder: applied now, not at the next save.
        if (config.StorageLimitGB != previous.StorageLimitGB || config.ResolvedClipsFolder != previous.ResolvedClipsFolder) _storage.Enforce();
        _log.Information("Settings applied");
    }

    private void RememberBufferFolder()
    {
        try
        {
            _paths.RememberBufferFolder(_config.BufferFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Could not note the buffer folder: {Error}", ex.Message);
        }
    }

    // The worker clears a folder it moves away from, but not one it wasn't running for, or whose drive was gone.
    // Before the worker starts, so nothing is writing. A folder with clips still to save stays until they are.
    private void ClearOldBuffers()
    {
        var current = _paths.BufferDirFor(_config);
        foreach (var dir in _paths.KnownBufferFolders().Select(GhostclipPaths.BufferDirIn).Append(_paths.BufferDir))
        {
            if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                foreach (var segment in SegmentRing.Scan(dir)) File.Delete(segment.Path);
                foreach (var session in AudioSync.Scan(dir)) AudioSync.Delete(dir, session);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Debug("Could not clear the old buffer in {Dir}: {Error}", dir, ex.Message);
            }
        }
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
        try
        {
            Shutdown();
        }
        finally
        {
            ExitThread();
        }
    }

    // Only the user's own Exit installs a downloaded update; the installer's
    // shutdown request (uninstall, update) goes through Exit above.
    private void ExitFromMenu()
    {
        try
        {
            Shutdown();
            if (UpdatePolicy.ApplyOnExit(_updates.IsReady, Library.LibraryProgram.IsOpen())) _updates.ApplyOnExit();
        }
        finally
        {
            // Whatever failed, never stay behind as a process with no icon that holds the hotkeys.
            ExitThread();
        }
    }

    private void OnSessionEnded(object? sender, Microsoft.Win32.SessionEndedEventArgs e) => Exit();

    private void Shutdown()
    {
        _shuttingDown = true;
        Microsoft.Win32.SystemEvents.SessionEnded -= OnSessionEnded;
        _icon.Visible = false;
        // A session left in Pending is finished into an MP4 on the next start, like any interrupted save.
        _session.Stop();
        _session.Dispose();
        _hotkey.Dispose();
        _foregroundTimer.Dispose();
        _diskTimer.Dispose();
        _updateTimer.Dispose();
        foreach (var (request, wait) in _commands)
        {
            wait.Unregister(null);
            request.Dispose();
        }
        _exitRequestWait.Unregister(null);
        _exitRequest.Dispose();
        _updateRequestWait.Unregister(null);
        _updateRequest.Dispose();
        _hotkeyCaptureWait.Unregister(null);
        _hotkeyCapture.Dispose();
        _hotkeyResume.Dispose();
        _configWatcher.Dispose();
        _configReload.Dispose();
        StatusFile.Delete(_paths.StatusFile);
        // Give the worker a moment to close its last segment cleanly.
        Task.Run(async () => await _worker.DisposeAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(8));
        _sound.Dispose();
        _mutedSound.Dispose();
        _unmutedSound.Dispose();
        _icon.Dispose();
    }
}
