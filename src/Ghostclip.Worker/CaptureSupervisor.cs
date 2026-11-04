using System.Globalization;
using System.Diagnostics;
using Ghostclip.Core;
using Ghostclip.Worker.Native;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ghostclip.Worker;

/// <summary>
/// Keeps the recorder running. FFmpeg exits on a resolution change, a lost
/// monitor, a D3D device loss or a driver reset; this loop restarts it with
/// backoff, re-probes the encoder when restarts keep failing fast, and kills
/// it when the frame counter stops moving.
/// </summary>
internal sealed class CaptureSupervisor : IAsyncDisposable
{
    private static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ClearPatience = TimeSpan.FromSeconds(10);

    private readonly GhostclipPaths _paths;
    private readonly IpcChannel _channel;
    private readonly LoggingLevelSwitch _level;
    private readonly ILogger _log;
    private readonly EncoderProber _prober;
    private readonly JobObject _job = new();
    private readonly ConfigInbox _configs = new();
    private readonly PauseSwitch _pause = new();
    private AudioCapture? _audio;
    private AudioCapture? _mic;
    private AudioCapture? _discord;
    private volatile uint _discordPid;
    private FfmpegRunner? _runner;
    private GhostclipConfig _config = new();
    private volatile WorkerState _state = WorkerState.Starting;
    private volatile string? _runId;
    private long _runStart;
    private int _runs;
    private string? _encoder;
    private string? _game;
    private uint _gamePid;
    private volatile bool _micMutedByHotkey;
    // The pipe, the heartbeat and the recorder loop all route audio: one at a time, or a stale mute could land last.
    private readonly Lock _routing = new();

    public CaptureSupervisor(GhostclipPaths paths, IpcChannel channel, LoggingLevelSwitch level, ILogger log)
    {
        _paths = paths;
        _channel = channel;
        _level = level;
        _log = log;
        _prober = new EncoderProber(paths, log);
    }

    public void ApplyConfig(GhostclipConfig config)
    {
        // The messages the tray shows from here follow the language too.
        UiText.Use(config.Language);
        _configs.Post(config);
    }

    /// <summary>The tray paused recording, or went on: see <see cref="WaitWhilePausedAsync"/>.</summary>
    public void SetPaused(bool paused) => _pause.Set(paused);

    /// <summary>The host saw a different program in the foreground.</summary>
    public void SetForeground(string? game, uint processId)
    {
        _game = game;
        _gamePid = processId;
        ApplyAudioRouting();
    }

    /// <summary>The mute hotkey was pressed: the microphone track records silence until it is pressed again.</summary>
    public void SetMicrophoneMuted(bool muted)
    {
        _micMutedByHotkey = muted;
        ApplyAudioRouting();
    }

    // Game-only audio, per-game and hotkey mic mute and Discord coming and going switch live: no restart, buffer kept.
    private void ApplyAudioRouting()
    {
        lock (_routing)
        {
            var config = _config;
            var gameOnly = config.AudioSourceFor(_game) == AudioSourceMode.GameOnly && _gamePid != 0;
            _audio?.SetTargetProcess(gameOnly ? _gamePid : 0);
            // On its own track, Discord is left out of the system one, so a call isn't recorded twice.
            _audio?.SetExcludedProcess(_discord is not null ? _discordPid : 0);
            _discord?.SetTargetProcess(_discordPid);
            if (_mic is not null) _mic.Muted = config.MicrophoneMutedFor(_game, _micMutedByHotkey);
        }
    }

    // Two process lookups every couple of seconds, only while there is a Discord track.
    private void FindDiscord()
    {
        if (_discord is null) return;
        var pid = DiscordProcess.Find();
        if (pid == _discordPid) return;
        _log.Information(pid != 0 ? "Discord found (pid {Pid})" : "Discord closed", pid);
        _discordPid = pid;
        ApplyAudioRouting();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _config = await _configs.TakeAsync(ct).ConfigureAwait(false);
        SetLogLevel(_config.LogLevel);
        DeleteStaleSegments();

        using var heartbeat = Task.Run(() => HeartbeatAsync(ct), ct);
        using var janitor = Task.Run(() => JanitorAsync(ct), ct);

        var forceProbe = false;
        var quickFailures = 0;
        var backoff = TimeSpan.FromSeconds(1);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_pause.MustStop)
                {
                    await WaitWhilePausedAsync(ct).ConfigureAwait(false);
                    continue;
                }

                var ffmpeg = FfmpegLocator.Find(_config.FfmpegPath, AppContext.BaseDirectory);
                if (ffmpeg is null)
                {
                    await FailAsync(UiText.L(
                        "FFmpeg was not found. Put it in the ffmpeg folder next to Ghostclip or set ffmpegPath in config.json.",
                        "O FFmpeg não foi encontrado. Coloque-o na pasta ffmpeg ao lado do Ghostclip ou defina ffmpegPath no config.json."), ct).ConfigureAwait(false);
                    continue;
                }

                _state = WorkerState.Probing;
                ProbeResult? choice;
                try
                {
                    choice = await _prober.ChooseAsync(ffmpeg, _config, forceProbe, ct).ConfigureAwait(false);
                }
                catch (DesktopUnavailableException)
                {
                    await WaitForDesktopAsync(ct).ConfigureAwait(false);
                    continue;
                }
                forceProbe = false;
                if (_pause.MustStop) continue;
                if (choice is null)
                {
                    // Never fall back to a CPU encoder: it would cost far more FPS than promised.
                    await FailAsync(UiText.L(
                        "No hardware encoder (NVENC, AMF or QuickSync) works on this PC. Ghostclip does not record with a CPU encoder.",
                        "Nenhum encoder de hardware (NVENC, AMF ou QuickSync) funciona neste PC. O Ghostclip não grava com encoder de CPU."), ct).ConfigureAwait(false);
                    continue;
                }

                var settings = CaptureSettings.From(_config, choice.Encoder, choice.Input, CaptureMonitor.HandleFor(_config, _log));
                _encoder = settings.Describe();
                var outcome = await RecordOnceAsync(ffmpeg, settings, ct).ConfigureAwait(false);
                if (outcome is Outcome.Reconfigured or Outcome.Paused || ct.IsCancellationRequested)
                {
                    backoff = TimeSpan.FromSeconds(1);
                    quickFailures = 0;
                    continue;
                }

                // FFmpeg died on its own.
                var uptime = _runner?.Uptime ?? TimeSpan.Zero;
                var errors = _runner?.LastErrors;
                if (EncoderProbe.IsDesktopDenied(errors))
                {
                    // Screen locked mid-recording: the buffer stays, recording resumes on unlock.
                    await WaitForDesktopAsync(ct).ConfigureAwait(false);
                    continue;
                }
                _state = WorkerState.Restarting;
                _log.Warning("Recorder exited after {Uptime:0.0} s; restarting in {Delay} s. Last errors: {Errors}",
                    uptime.TotalSeconds, backoff.TotalSeconds, errors);
                if (uptime < TimeSpan.FromSeconds(10) && ++quickFailures >= 3)
                {
                    // The cached encoder choice may no longer be valid (driver update, GPU swap).
                    forceProbe = true;
                    quickFailures = 0;
                }
                if (uptime > TimeSpan.FromSeconds(30))
                {
                    backoff = TimeSpan.FromSeconds(1);
                }
                await SendAsync(new IpcMessage { Type = IpcType.Error, Message = $"Recording stopped ({errors}). Restarting." }, ct).ConfigureAwait(false);
                var pauseChanged = _pause.Changed;
                if (!_pause.MustStop) await Task.WhenAny(Task.Delay(backoff, ct), pauseChanged).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            _state = WorkerState.Stopped;
            await StopRecorderAsync().ConfigureAwait(false);
        }
    }

    private enum Outcome { Exited, Reconfigured, Paused }

    private async Task<Outcome> RecordOnceAsync(string ffmpeg, CaptureSettings settings, CancellationToken ct)
    {
        var pipes = new List<string>();
        var titles = new List<string>();
        AudioCapture? first = null;
        if (_config.SystemAudio)
        {
            _audio ??= new AudioCapture(_log, AudioTrack.Main);
            pipes.Add(_audio.BeginSession());
            titles.Add("Game");
            first = _audio;
        }
        else
        {
            _audio?.Dispose();
            _audio = null;
        }
        if (_config.Microphone)
        {
            _mic ??= new AudioCapture(_log, AudioTrack.Microphone);
            pipes.Add(_mic.BeginSession());
            titles.Add("Microphone");
            first ??= _mic;
        }
        else
        {
            _mic?.Dispose();
            _mic = null;
        }
        if (_config.DiscordTrack)
        {
            _discord ??= new AudioCapture(_log, AudioTrack.Discord);
            pipes.Add(_discord.BeginSession());
            titles.Add("Discord");
            first ??= _discord;
            _discordPid = DiscordProcess.Find();
        }
        else
        {
            _discord?.Dispose();
            _discord = null;
            _discordPid = 0;
        }
        ApplyAudioRouting();

        var start = SegmentRing.NextStartNumber(SegmentRing.Scan(_paths.BufferDir));
        _runStart = start;
        _runId = string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}:{++_runs}");
        var args = FfmpegArgs.Record(settings, _paths.BufferDir, start, pipes, _config.AudioOffsetMs);
        _runner?.Dispose();
        _runner = FfmpegRunner.Start(ffmpeg, args, _job, _log);
        // The game first: capture and encoding wait behind its frames on the CPU and GPU.
        _runner.ApplyPriority(RecorderPriority.For(_config.GamePriority));
        _state = WorkerState.Recording;
        _log.Information("Recording with {Encoder}, {Fps} fps, {Bitrate} Mbps, audio tracks [{Tracks}], from segment {Start}",
            settings.Describe(), settings.Fps, settings.BitrateMbps, string.Join(", ", titles), start);

        var nextConfig = _configs.NextAsync(ct);
        var syncMeasured = first is null;
        while (true)
        {
            var pauseChanged = _pause.Changed;
            if (_pause.MustStop)
            {
                await StopRecorderAsync().ConfigureAwait(false);
                return Outcome.Paused;
            }

            var done = await Task.WhenAny(_runner.Exited, nextConfig, pauseChanged, Task.Delay(1000, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (!syncMeasured && _runner.FirstSample is { } sample && first!.StreamStartTicks is var audioStart and > 0)
            {
                var lead = AudioSync.MeasureLeadMs((sample.Ticks - audioStart) / (double)Stopwatch.Frequency, sample.Frames, settings.Fps);
                AudioSync.Write(_paths.BufferDir, new RecordingSession(start, lead, titles.Count > 1 ? titles.ToArray() : null));
                _log.Information("Audio stream started {Lead} ms before video; saves will compensate", lead);
                syncMeasured = true;
            }

            if (done == _runner.Exited)
            {
                _audio?.EndSession();
                _mic?.EndSession();
                _discord?.EndSession();
                return Outcome.Exited;
            }

            if (done == nextConfig)
            {
                var config = await _configs.TakeAsync(ct).ConfigureAwait(false);
                var restart = !config.SameCaptureAs(_config);
                if (config.GamePriority != _config.GamePriority) _runner.ApplyPriority(RecorderPriority.For(config.GamePriority));
                _config = config;
                SetLogLevel(config.LogLevel);
                ApplyAudioRouting();
                if (restart)
                {
                    _log.Information("Capture settings changed; restarting the recorder with an empty buffer");
                    await StopRecorderAsync().ConfigureAwait(false);
                    // Segments in two formats can't be joined by stream copy.
                    ClearBuffer();
                    return Outcome.Reconfigured;
                }
                nextConfig = _configs.NextAsync(ct);
                continue;
            }

            if (_runner.Uptime > StartGrace && _runner.SinceLastFrame > StallLimit)
            {
                _log.Warning("No new frames for {Seconds:0} s; killing the recorder", _runner.SinceLastFrame.TotalSeconds);
                _runner.Kill();
            }
        }
    }

    // Nothing is captured and the buffer is deleted until the tray says to go on, even if it already has. Settings
    // saved meanwhile apply right away: with no buffer there is nothing to restart. A segment a save is still reading
    // is deleted on a later pass; recording doesn't go on beside it for a while, or the next clip would include it.
    private async Task WaitWhilePausedAsync(CancellationToken ct)
    {
        _state = WorkerState.Paused;
        _log.Information("Recording paused; deleting the replay buffer");
        lock (_routing)
        {
            // Closed, not only unused: Windows stops showing the microphone as in use.
            _audio?.Dispose();
            _audio = null;
            _mic?.Dispose();
            _mic = null;
            _discord?.Dispose();
            _discord = null;
            _discordPid = 0;
        }
        var sinceResume = Stopwatch.StartNew();
        while (true)
        {
            var changed = _pause.Changed;
            _pause.TakeBufferClear();
            var left = ClearBuffer();
            if (_pause.Paused)
            {
                sinceResume.Restart();
            }
            else if (!_pause.MustStop && (left == 0 || sinceResume.Elapsed > ClearPatience))
            {
                if (left > 0) _log.Warning("{Left} segments could not be deleted after pausing", left);
                break;
            }
            var nextConfig = _configs.NextAsync(ct);
            if (await Task.WhenAny(changed, nextConfig, Task.Delay(left > 0 ? 250 : 2000, ct)).ConfigureAwait(false) == nextConfig)
            {
                _config = await _configs.TakeAsync(ct).ConfigureAwait(false);
                SetLogLevel(_config.LogLevel);
            }
            ct.ThrowIfCancellationRequested();
        }
        _log.Information("Recording resumed");
    }

    // Numbering starts over with an empty buffer, so what described the old segments goes too. Returns the segments left.
    private int ClearBuffer()
    {
        DeleteSegments(SegmentRing.Scan(_paths.BufferDir));
        foreach (var session in AudioSync.Scan(_paths.BufferDir)) AudioSync.Delete(_paths.BufferDir, session);
        return SegmentRing.Scan(_paths.BufferDir).Count;
    }

    // Quiet wait while Windows refuses the desktop: no FFmpeg runs, no restart
    // notifications, no backoff growth. A cheap OpenInputDesktop poll says when
    // the user is back; only then is capture tried again.
    private async Task WaitForDesktopAsync(CancellationToken ct)
    {
        _state = WorkerState.WaitingForDesktop;
        _log.Information("Desktop unavailable (locked or secure screen); waiting");
        var waited = Stopwatch.StartNew();
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        while (!InputDesktop.IsAvailable() && !_pause.MustStop);
        // Give the unlock animation a moment before Desktop Duplication starts.
        await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        _log.Information("Desktop available again after {Seconds:0} s", waited.Elapsed.TotalSeconds);
    }

    private async Task FailAsync(string message, CancellationToken ct)
    {
        _log.Error("{Message}", message);
        _state = WorkerState.Stopped;
        await SendAsync(new IpcMessage { Type = IpcType.Error, Message = message, Fatal = true }, ct).ConfigureAwait(false);
        // Nothing to do until the user changes something, or pauses: the buffer still has to go.
        var pauseChanged = _pause.Changed;
        if (_pause.MustStop) return;
        var nextConfig = _configs.NextAsync(ct);
        if (await Task.WhenAny(nextConfig, pauseChanged).ConfigureAwait(false) != nextConfig) return;
        _config = await _configs.TakeAsync(ct).ConfigureAwait(false);
        SetLogLevel(_config.LogLevel);
    }

    private async Task StopRecorderAsync()
    {
        if (_runner is not null)
        {
            // The benchmark reads these counts from the log.
            _log.Information("Recorder stopped after {Uptime:0} s: {Frames} frames, {Dropped} dropped, {Duplicated} duplicated",
                _runner.Uptime.TotalSeconds, _runner.Frames, _runner.Dropped, _runner.Duplicated);
            await _runner.StopAsync().ConfigureAwait(false);
            _runner.Dispose();
            _runner = null;
        }
        _audio?.EndSession();
        _mic?.EndSession();
        _discord?.EndSession();
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var beat = 0;
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (++beat % 2 == 0) FindDiscord();
            var r = _runner;
            var status = new WorkerStatus
            {
                State = _state,
                Encoder = _encoder,
                Frames = r?.Frames ?? 0,
                Fps = r?.Fps ?? 0,
                Dropped = r?.Dropped ?? 0,
                Duplicated = r?.Duplicated ?? 0,
                Audio = _audio is not null,
                AudioRoute = _audio?.Route,
                Microphone = _mic is { Muted: false },
                MicrophoneMuted = _mic is { Muted: true },
                Discord = _discord is not null && _discordPid != 0,
                RunId = _runId,
                RunStart = _runStart,
            };
            await SendAsync(new IpcMessage { Type = IpcType.Status, Status = status }, ct).ConfigureAwait(false);
        }
    }

    // Rotation: keeps a clip's worth of segments plus slack, deletes the rest.
    private async Task JanitorAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            // The longest clip any game profile can ask for.
            var keep = SegmentRing.KeepCount(_config.MaxClipSeconds, GhostclipConfig.SegmentSeconds);
            var segments = SegmentRing.Scan(_paths.BufferDir);
            DeleteSegments(SegmentRing.SelectForDeletion(segments, keep));
            var oldest = segments.Count > keep ? segments[^keep].Sequence : segments.FirstOrDefault().Sequence;
            foreach (var session in AudioSync.SelectObsolete(AudioSync.Scan(_paths.BufferDir), oldest))
            {
                AudioSync.Delete(_paths.BufferDir, session);
            }
        }
    }

    private void DeleteStaleSegments()
    {
        // Segments older than a clip (plus a margin) are from a session long gone.
        var maxAge = TimeSpan.FromSeconds(_config.MaxClipSeconds + 120);
        DeleteSegments(SegmentRing.SelectStale(SegmentRing.Scan(_paths.BufferDir), DateTime.UtcNow, maxAge));
    }

    private void DeleteSegments(IEnumerable<SegmentFile> segments)
    {
        foreach (var s in segments)
        {
            try
            {
                File.Delete(s.Path);
            }
            catch (IOException ex)
            {
                // Usually a save is reading it through a hard link; the next pass retries.
                _log.Debug("Could not delete {Segment} yet: {Error}", s.Path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.Debug("Could not delete {Segment} yet: {Error}", s.Path, ex.Message);
            }
        }
    }

    private void SetLogLevel(GhostclipLogLevel level) => _level.MinimumLevel = level switch
    {
        GhostclipLogLevel.Debug => LogEventLevel.Debug,
        GhostclipLogLevel.Warning => LogEventLevel.Warning,
        GhostclipLogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information,
    };

    private async Task SendAsync(IpcMessage message, CancellationToken ct)
    {
        try
        {
            await _channel.SendAsync(message, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Host gone: Program notices the closed pipe and shuts down.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopRecorderAsync().ConfigureAwait(false);
        _audio?.Dispose();
        _mic?.Dispose();
        _discord?.Dispose();
        _job.Dispose();
    }
}
