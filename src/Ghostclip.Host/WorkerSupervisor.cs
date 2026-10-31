using System.Diagnostics;
using System.IO.Pipes;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>
/// Starts the capture worker, talks to it over a named pipe and restarts it if
/// it crashes or stops answering. The buffer lives on disk, so a restarted
/// worker picks up where the old one stopped.
/// </summary>
internal sealed class WorkerSupervisor(ILogger log) : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _cts = new();
    private volatile GhostclipConfig _config = new();
    private volatile IpcChannel? _channel;
    private Process? _process;
    private Task? _loop;

    /// <summary>Raised on a thread-pool thread for every heartbeat.</summary>
    public event Action<WorkerStatus>? StatusChanged;

    /// <summary>Raised on a thread-pool thread. The flag says the worker can't record until the config changes.</summary>
    public event Action<string, bool>? ErrorReported;

    public void Start(GhostclipConfig config)
    {
        _config = ForWorker(config);
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    // The worker runs without culture data, so it can't tell what Windows speaks: it gets the language already decided.
    private static GhostclipConfig ForWorker(GhostclipConfig config) =>
        config with { Language = UiText.Portuguese ? UiLanguage.Portuguese : UiLanguage.English };

    private volatile IpcMessage _foreground = new() { Type = IpcType.Foreground, Game = ClipNaming.FallbackGame };

    /// <summary>Tells the worker which game is in front, for game-only audio and per-game mic mute.</summary>
    public void SetForeground(string game, uint processId)
    {
        _foreground = new IpcMessage { Type = IpcType.Foreground, Game = game, ProcessId = processId };
        if (_channel is { } channel)
        {
            _ = SendQuietlyAsync(channel, _foreground);
        }
    }

    public void UpdateConfig(GhostclipConfig config)
    {
        _config = ForWorker(config);
        if (_channel is { } channel)
        {
            _ = SendQuietlyAsync(channel, new IpcMessage { Type = IpcType.Config, Config = _config });
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Ghostclip.Worker.exe");
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            if (!File.Exists(exe))
            {
                ErrorReported?.Invoke(L($"The capture worker is missing ({exe}). Reinstall Ghostclip.",
                    $"O processo de captura não foi encontrado ({exe}). Reinstale o Ghostclip."), true);
                return;
            }

            var started = Stopwatch.StartNew();
            await RunWorkerOnceAsync(exe, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;

            if (started.Elapsed > TimeSpan.FromMinutes(1)) backoff = TimeSpan.FromSeconds(1);
            log.Warning("Worker ended after {Seconds:0} s; restarting in {Delay} s", started.Elapsed.TotalSeconds, backoff.TotalSeconds);
            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task RunWorkerOnceAsync(string exe, CancellationToken ct)
    {
        var name = $"ghostclip-{Environment.ProcessId}-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--pipe");
        psi.ArgumentList.Add(name);
        using var process = Process.Start(psi);
        if (process is null)
        {
            log.Error("Could not start the worker");
            return;
        }
        _process = process;
        log.Information("Worker started (pid {Pid})", process.Id);

        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await server.WaitForConnectionAsync(connect.Token).ConfigureAwait(false);
            }

            await using var channel = new IpcChannel(server);
            await channel.SendAsync(new IpcMessage { Type = IpcType.Config, Config = _config }, ct).ConfigureAwait(false);
            await channel.SendAsync(_foreground, ct).ConfigureAwait(false);
            _channel = channel;

            var lastMessage = Stopwatch.StartNew();
            using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watch = Task.Run(async () =>
            {
                while (!watchdog.IsCancellationRequested)
                {
                    await Task.Delay(1000, watchdog.Token).ConfigureAwait(false);
                    if (lastMessage.Elapsed > HeartbeatTimeout)
                    {
                        log.Warning("Worker stopped answering for {Seconds:0} s; killing it", lastMessage.Elapsed.TotalSeconds);
                        Kill(process);
                        return;
                    }
                }
            }, watchdog.Token);

            try
            {
                while (await channel.ReceiveAsync(ct).ConfigureAwait(false) is { } message)
                {
                    lastMessage.Restart();
                    if (message.Type == IpcType.Status && message.Status is { } status)
                    {
                        StatusChanged?.Invoke(status);
                    }
                    else if (message.Type == IpcType.Error && message.Message is { } text)
                    {
                        log.Warning("Worker: {Message}", text);
                        ErrorReported?.Invoke(text, message.Fatal);
                    }
                }
            }
            catch (IOException)
            {
                // Worker died: the pipe broke.
            }
            await watchdog.CancelAsync().ConfigureAwait(false);
            try { await watch.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.Warning("Worker did not connect within 10 s");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _channel = null;
            if (!ct.IsCancellationRequested) Kill(process);
            _process = null;
        }
        StatusChanged?.Invoke(new WorkerStatus { State = WorkerState.Restarting });
    }

    /// <summary>Asks the worker to stop cleanly, then kills it if it doesn't.</summary>
    public async ValueTask DisposeAsync()
    {
        var process = _process;
        if (_channel is { } channel)
        {
            await SendQuietlyAsync(channel, new IpcMessage { Type = IpcType.Shutdown }).ConfigureAwait(false);
        }
        if (process is not null && !process.HasExited)
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(wait.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Kill(process); }
        }
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        }
        _cts.Dispose();
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private async Task SendQuietlyAsync(IpcChannel channel, IpcMessage message)
    {
        try
        {
            await channel.SendAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            log.Debug("Could not reach the worker: {Error}", ex.Message);
        }
    }
}
