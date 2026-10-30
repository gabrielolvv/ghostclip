using System.Diagnostics;
using System.Threading.Channels;
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

    private readonly GhostclipPaths _paths;
    private readonly IpcChannel _channel;
    private readonly LoggingLevelSwitch _level;
    private readonly ILogger _log;
    private readonly EncoderProber _prober;
    private readonly JobObject _job = new();
    private readonly Channel<GhostclipConfig> _configs = Channel.CreateUnbounded<GhostclipConfig>();
    private AudioCapture? _audio;
    private AudioCapture? _mic;
    private FfmpegRunner? _runner;
    private GhostclipConfig _config = new();
    private volatile WorkerState _state = WorkerState.Starting;
    private string? _encoder;
    private string? _game;
    private uint _gamePid;

    public CaptureSupervisor(GhostclipPaths paths, IpcChannel channel, LoggingLevelSwitch level, ILogger log)
    {
        _paths = paths;
        _channel = channel;
        _level = level;
        _log = log;
        _prober = new EncoderProber(paths, log);
    }

    public void ApplyConfig(GhostclipConfig config) => _configs.Writer.TryWrite(config);

    /// <summary>The host saw a different program in the foreground.</summary>
    public void SetForeground(string? game, uint processId)
    {
        _game = game;
        _gamePid = processId;
        ApplyAudioRouting();
    }

    // Game-only audio and per-game mic mute switch live: no restart, buffer kept.
    private void ApplyAudioRouting()
    {
        var config = _config;
        var gameOnly = config.AudioSourceFor(_game) == AudioSourceMode.GameOnly && _gamePid != 0;
        _audio?.SetTargetProcess(gameOnly ? _gamePid : 0);
        if (_mic is not null) _mic.Muted = config.MicrophoneMutedFor(_game);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _config = await _configs.Reader.ReadAsync(ct).ConfigureAwait(false);
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
                var ffmpeg = FfmpegLocator.Find(_config.FfmpegPath, AppContext.BaseDirectory);
                if (ffmpeg is null)
                {
                    await FailAsync("FFmpeg was not found. Put it in the ffmpeg folder next to Ghostclip or set ffmpegPath in config.json.", ct).ConfigureAwait(false);
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
                if (choice is null)
                {
                    // Never fall back to a CPU encoder: it would cost far more FPS than promised.
                    await FailAsync("No hardware encoder (NVENC, AMF or QuickSync) works on this PC. Ghostclip does not record with a CPU encoder.", ct).ConfigureAwait(false);
                    continue;
                }

                var settings = CaptureSettings.From(_config, choice.Encoder, choice.Input);
                _encoder = settings.Describe();
                var outcome = await RecordOnceAsync(ffmpeg, settings, ct).ConfigureAwait(false);
                if (outcome == Outcome.Reconfigured || ct.IsCancellationRequested)
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
                await Task.Delay(backoff, ct).ConfigureAwait(false);
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

    private enum Outcome { Exited, Reconfigured }

    private async Task<Outcome> RecordOnceAsync(string ffmpeg, CaptureSettings settings, CancellationToken ct)
    {
        var pipes = new List<string>();
        var titles = new List<string>();
        AudioCapture? first = null;
        if (_config.SystemAudio)
        {
            _audio ??= new AudioCapture(_log, microphone: false);
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
            _mic ??= new AudioCapture(_log, microphone: true);
            pipes.Add(_mic.BeginSession());
            titles.Add("Microphone");
            first ??= _mic;
        }
        else
        {
            _mic?.Dispose();
            _mic = null;
        }
        ApplyAudioRouting();

        var start = SegmentRing.NextStartNumber(SegmentRing.Scan(_paths.BufferDir));
        var args = FfmpegArgs.Record(settings, _paths.BufferDir, start, pipes, _config.AudioOffsetMs);
        _runner?.Dispose();
        _runner = FfmpegRunner.Start(ffmpeg, args, _job, _log);
        _state = WorkerState.Recording;
        _log.Information("Recording with {Encoder}, {Fps} fps, {Bitrate} Mbps, audio tracks [{Tracks}], from segment {Start}",
            settings.Describe(), settings.Fps, settings.BitrateMbps, string.Join(", ", titles), start);

        var nextConfig = _configs.Reader.ReadAsync(ct).AsTask();
        var syncMeasured = first is null;
        while (true)
        {
            var done = await Task.WhenAny(_runner.Exited, nextConfig, Task.Delay(1000, ct)).ConfigureAwait(false);
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
                return Outcome.Exited;
            }

            if (done == nextConfig)
            {
                var config = await nextConfig.ConfigureAwait(false);
                var restart = !config.SameCaptureAs(_config);
                _config = config;
                SetLogLevel(config.LogLevel);
                ApplyAudioRouting();
                if (restart)
                {
                    _log.Information("Capture settings changed; restarting the recorder with an empty buffer");
                    await StopRecorderAsync().ConfigureAwait(false);
                    // Segments in two formats can't be joined by stream copy.
                    DeleteSegments(SegmentRing.Scan(_paths.BufferDir));
                    return Outcome.Reconfigured;
                }
                nextConfig = _configs.Reader.ReadAsync(ct).AsTask();
                continue;
            }

            if (_runner.Uptime > StartGrace && _runner.SinceLastFrame > StallLimit)
            {
                _log.Warning("No new frames for {Seconds:0} s; killing the recorder", _runner.SinceLastFrame.TotalSeconds);
                _runner.Kill();
            }
        }
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
        while (!InputDesktop.IsAvailable());
        // Give the unlock animation a moment before Desktop Duplication starts.
        await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        _log.Information("Desktop available again after {Seconds:0} s", waited.Elapsed.TotalSeconds);
    }

    private async Task FailAsync(string message, CancellationToken ct)
    {
        _log.Error("{Message}", message);
        _state = WorkerState.Stopped;
        await SendAsync(new IpcMessage { Type = IpcType.Error, Message = message, Fatal = true }, ct).ConfigureAwait(false);
        // Nothing to do until the user changes something.
        _config = await _configs.Reader.ReadAsync(ct).ConfigureAwait(false);
        SetLogLevel(_config.LogLevel);
    }

    private async Task StopRecorderAsync()
    {
        if (_runner is not null)
        {
            await _runner.StopAsync().ConfigureAwait(false);
            _runner.Dispose();
            _runner = null;
        }
        _audio?.EndSession();
        _mic?.EndSession();
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
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
        _job.Dispose();
    }
}
