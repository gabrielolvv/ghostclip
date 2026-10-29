using System.Diagnostics;
using System.Globalization;
using Ghostclip.Worker.Native;
using Serilog;

namespace Ghostclip.Worker;

internal sealed record ProgressSample(long Ticks, long Frames);

/// <summary>One recording FFmpeg process: progress from stdout, warnings from stderr.</summary>
internal sealed class FfmpegRunner : IDisposable
{
    private readonly Process _process;
    private readonly ILogger _log;
    private readonly Queue<string> _lastErrors = new();
    private long _frames;
    private long _dropped;
    private long _duplicated;
    private double _fps;
    private long _lastFrameAdvanceTicks = Stopwatch.GetTimestamp();

    private FfmpegRunner(Process process, ILogger log)
    {
        _process = process;
        _log = log;
        StartedTicks = Stopwatch.GetTimestamp();
        Exited = process.WaitForExitAsync().ContinueWith(_ => process.ExitCode, TaskScheduler.Default);
        _ = Task.Run(ReadProgressAsync);
        _ = Task.Run(ReadErrorsAsync);
    }

    public Task<int> Exited { get; }
    public long StartedTicks { get; }
    public long Frames => Interlocked.Read(ref _frames);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Duplicated => Interlocked.Read(ref _duplicated);
    public double Fps => Volatile.Read(ref _fps);

    public TimeSpan Uptime => Stopwatch.GetElapsedTime(StartedTicks);

    /// <summary>When the first progress report arrived (QPC ticks) and how many frames it counted.</summary>
    public ProgressSample? FirstSample { get; private set; }

    /// <summary>Time since the frame counter last moved. A hung capture shows up here.</summary>
    public TimeSpan SinceLastFrame => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastFrameAdvanceTicks));

    public string LastErrors
    {
        get { lock (_lastErrors) return string.Join(" | ", _lastErrors); }
    }

    public static FfmpegRunner Start(string exe, IReadOnlyList<string> args, JobObject job, ILogger log)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        try
        {
            job.Assign(process.Handle);
        }
        catch
        {
            process.Kill();
            process.Dispose();
            throw;
        }
        log.Debug("ffmpeg {Args}", string.Join(' ', args));
        return new FfmpegRunner(process, log);
    }

    /// <summary>Asks FFmpeg to finish the current segment cleanly, then kills it if it doesn't.</summary>
    public async Task StopAsync()
    {
        if (_process.HasExited) return;
        try
        {
            await _process.StandardInput.WriteAsync('q').ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // stdin already closed: fall through to the kill.
        }
        if (await Task.WhenAny(Exited, Task.Delay(3000)).ConfigureAwait(false) != Exited)
        {
            Kill();
        }
    }

    public void Kill()
    {
        try { _process.Kill(); } catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (!_process.HasExited) Kill();
        _process.Dispose();
    }

    // -progress pipe:1 writes key=value blocks, one per stats period.
    private async Task ReadProgressAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.AsSpan(0, eq);
                var value = line.AsSpan(eq + 1).Trim();
                if (key.SequenceEqual("frame") && long.TryParse(value, CultureInfo.InvariantCulture, out var f))
                {
                    if (f > 0 && FirstSample is null)
                    {
                        FirstSample = new ProgressSample(Stopwatch.GetTimestamp(), f);
                    }
                    if (f > Interlocked.Exchange(ref _frames, f))
                    {
                        Interlocked.Exchange(ref _lastFrameAdvanceTicks, Stopwatch.GetTimestamp());
                    }
                }
                else if (key.SequenceEqual("fps") && double.TryParse(value, CultureInfo.InvariantCulture, out var fps))
                {
                    Volatile.Write(ref _fps, fps);
                }
                else if (key.SequenceEqual("drop_frames") && long.TryParse(value, CultureInfo.InvariantCulture, out var d))
                {
                    Interlocked.Exchange(ref _dropped, d);
                }
                else if (key.SequenceEqual("dup_frames") && long.TryParse(value, CultureInfo.InvariantCulture, out var u))
                {
                    Interlocked.Exchange(ref _duplicated, u);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private async Task ReadErrorsAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0) continue;
                _log.Warning("ffmpeg: {Line}", line);
                lock (_lastErrors)
                {
                    _lastErrors.Enqueue(line);
                    while (_lastErrors.Count > 5) _lastErrors.Dequeue();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }
}
