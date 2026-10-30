using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Ghostclip.Core;
using Ghostclip.Worker.Native;
using Serilog;

namespace Ghostclip.Worker;

/// <summary>
/// One audio track: WASAPI capture fed to FFmpeg as raw 48 kHz float stereo
/// over a named pipe.
///
/// The main track records either the whole playback mix (loopback) or just the
/// game's process tree (process loopback); <see cref="SetTargetProcess"/>
/// switches between them live, on the same pipe, so changing games never
/// restarts the recorder. The microphone track records the default capture
/// device and can be muted per game, which records silence instead.
///
/// Loopback delivers nothing while nothing plays. A raw PCM input that stops
/// would stall FFmpeg's muxer and with it the video, so gaps are filled with
/// silence against a QueryPerformanceCounter clock: the track always advances
/// at real time.
/// </summary>
internal sealed class AudioCapture : IDisposable
{
    private const int SampleRate = FfmpegArgs.AudioSampleRate;
    private const int BytesPerFrame = FfmpegArgs.AudioBytesPerFrame;
    private const long BufferDuration100ns = 1_000_000; // 100 ms device buffer
    private const int MaxSilenceGapFrames = SampleRate * 30 / 1000;

    private readonly ILogger _log;
    private readonly bool _microphone;
    private readonly string _name;
    private readonly Thread _thread;
    private readonly object _sync = new();
    // Two seconds between capture and pipe: absorbs FFmpeg stalls without dropping.
    private readonly byte[] _ring = new byte[SampleRate * BytesPerFrame * 2];
    private readonly AutoResetEvent _dataReady = new(false);
    private readonly AutoResetEvent _deviceEvent = new(false);
    private int _ringStart;
    private int _ringCount;
    private bool _streaming;
    private long _clockStart;
    private long _framesQueued;
    private long _overflowBytes;
    private long _streamStartTicks;
    private volatile bool _stop;
    private volatile bool _muted;
    private volatile uint _targetPid;
    private volatile string _route = "starting";
    private PipeSession? _session;
    private string? _lastError;

    public AudioCapture(ILogger log, bool microphone)
    {
        _log = log;
        _microphone = microphone;
        _name = microphone ? "microphone" : "audio";
        _thread = new Thread(Run) { IsBackground = true, Name = $"capture-{_name}", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>What is being recorded right now ("system", "game (pid 1234)", "silence").</summary>
    public string Route => _route;

    /// <summary>QPC ticks of the first sample FFmpeg read in this session; 0 until then.</summary>
    public long StreamStartTicks => Volatile.Read(ref _streamStartTicks);

    /// <summary>Game-only audio: capture this process tree. 0 goes back to the whole system mix.</summary>
    public void SetTargetProcess(uint processId) => _targetPid = processId;

    /// <summary>Records silence instead of the device, keeping the track's timeline intact.</summary>
    public bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    /// <summary>Creates the pipe for a new FFmpeg run and returns its path.</summary>
    public string BeginSession()
    {
        EndSession();
        var name = $"ghostclip-{_name}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        _session = new PipeSession(this, name, _log);
        return $@"\\.\pipe\{name}";
    }

    public void EndSession()
    {
        _session?.Dispose();
        _session = null;
        Volatile.Write(ref _streamStartTicks, 0);
        lock (_sync)
        {
            _streaming = false;
            _ringCount = 0;
        }
    }

    public void Dispose()
    {
        EndSession();
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(2));
        _dataReady.Dispose();
        _deviceEvent.Dispose();
    }

    // Called once FFmpeg has started reading. Everything captured before that is
    // dropped: queuing it would put audio ahead of the video.
    private void StartStreaming()
    {
        lock (_sync)
        {
            _ringStart = 0;
            _ringCount = 0;
            _framesQueued = 0;
            _clockStart = Stopwatch.GetTimestamp();
            _streaming = true;
        }
        Volatile.Write(ref _streamStartTicks, _clockStart);
    }

    private void Enqueue(ReadOnlySpan<byte> data, bool silence)
    {
        lock (_sync)
        {
            if (!_streaming) return;
            var free = _ring.Length - _ringCount;
            if (data.Length > free)
            {
                // FFmpeg stopped reading for over 2 s: drop the oldest audio.
                var drop = data.Length - free;
                _ringStart = (_ringStart + drop) % _ring.Length;
                _ringCount -= drop;
                _overflowBytes += drop;
            }
            var tail = (_ringStart + _ringCount) % _ring.Length;
            var first = Math.Min(data.Length, _ring.Length - tail);
            if (silence)
            {
                Array.Clear(_ring, tail, first);
                Array.Clear(_ring, 0, data.Length - first);
            }
            else
            {
                data[..first].CopyTo(_ring.AsSpan(tail));
                data[first..].CopyTo(_ring);
            }
            _ringCount += data.Length;
            _framesQueued += data.Length / BytesPerFrame;
        }
        _dataReady.Set();
    }

    private void EnqueueSilence(long frames)
    {
        Span<byte> zeros = stackalloc byte[4096];
        zeros.Clear();
        var bytes = frames * BytesPerFrame;
        while (bytes > 0)
        {
            var n = (int)Math.Min(bytes, zeros.Length);
            Enqueue(zeros[..n], silence: true);
            bytes -= n;
        }
    }

    private int Take(byte[] buffer, int timeoutMs)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            lock (_sync)
            {
                if (_ringCount > 0)
                {
                    var n = Math.Min(buffer.Length, _ringCount);
                    var first = Math.Min(n, _ring.Length - _ringStart);
                    Buffer.BlockCopy(_ring, _ringStart, buffer, 0, first);
                    Buffer.BlockCopy(_ring, 0, buffer, first, n - first);
                    _ringStart = (_ringStart + n) % _ring.Length;
                    _ringCount -= n;
                    return n;
                }
            }
            _dataReady.WaitOne(timeoutMs);
        }
        return 0;
    }

    // Fills silence up to the wall clock when the device delivered nothing.
    private void PadToClock(bool gotPackets)
    {
        long missing;
        lock (_sync)
        {
            if (!_streaming) return;
            var elapsed = Stopwatch.GetTimestamp() - _clockStart;
            var expected = elapsed * SampleRate / Stopwatch.Frequency;
            missing = expected - _framesQueued;
        }
        if (!gotPackets && missing > MaxSilenceGapFrames)
        {
            EnqueueSilence(missing);
        }
        if (Interlocked.Exchange(ref _overflowBytes, 0) is var dropped and > 0)
        {
            _log.Warning("{Track} pipe stalled; dropped {Ms} ms of audio", _name, dropped / BytesPerFrame * 1000 / SampleRate);
        }
    }

    // What the capture thread should have open: a device id, or a process id for game-only audio.
    private readonly record struct Source(string? DeviceId, uint ProcessId);

    private void Run()
    {
        Wasapi.InitializeThread();
        IMMDeviceEnumerator? enumerator = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        Source open = default;
        long retryAt = 0;
        long nextCheck = 0;
        uint failedPid = 0;

        while (!_stop)
        {
            var now = Stopwatch.GetTimestamp();
            try
            {
                enumerator ??= Wasapi.CreateEnumerator();
                var pid = _microphone ? 0 : _targetPid;
                if (pid == failedPid && pid != 0) pid = 0; // game-only failed for this game: use the system mix

                // Re-check once a second what should be open: cheaper and simpler than COM callbacks.
                if (client is not null && now >= nextCheck)
                {
                    nextCheck = now + Stopwatch.Frequency;
                    var wanted = pid != 0 ? new Source(null, pid) : new Source(DefaultDeviceId(enumerator), 0);
                    if (wanted != open)
                    {
                        _log.Information("{Track}: switching source", _name);
                        Close(ref client, ref capture);
                    }
                }
                if (client is null && now >= retryAt)
                {
                    try
                    {
                        (client, capture, open) = pid != 0 ? OpenProcess(pid) : OpenDevice(enumerator);
                    }
                    catch (Exception ex) when (pid != 0 && ex is COMException or TimeoutException or InvalidCastException)
                    {
                        // Older Windows or a protected process: fall back rather than record nothing.
                        _log.Warning("Game-only audio unavailable for pid {Pid} ({Error}); recording system audio", pid, ex.Message);
                        failedPid = pid;
                        continue;
                    }
                    _route = open.ProcessId != 0 ? $"game (pid {open.ProcessId})" : _microphone ? "microphone" : "system";
                    _lastError = null;
                    _log.Information("{Track}: capturing {Route}", _name, _route);
                }
                if (_targetPid != failedPid) failedPid = 0;

                var got = capture is not null && Drain(capture);
                PadToClock(got);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Close(ref client, ref capture);
                retryAt = now + Stopwatch.Frequency / 2;
                _route = "silence";
                if (_lastError != ex.Message)
                {
                    _lastError = ex.Message;
                    _log.Warning("{Track} unavailable ({Error}); recording silence until a device appears", _name, ex.Message);
                }
                PadToClock(gotPackets: false);
            }
            Thread.Sleep(10);
        }

        Close(ref client, ref capture);
        Wasapi.Release(enumerator);
    }

    private static void Close(ref IAudioClient? client, ref IAudioCaptureClient? capture)
    {
        client?.Stop();
        Wasapi.Release(capture);
        Wasapi.Release(client);
        capture = null;
        client = null;
    }

    private (IAudioClient, IAudioCaptureClient, Source) OpenDevice(IMMDeviceEnumerator enumerator)
    {
        var flow = _microphone ? Wasapi.eCapture : Wasapi.eRender;
        Wasapi.Check(enumerator.GetDefaultAudioEndpoint(flow, Wasapi.eConsole, out var device));
        try
        {
            Wasapi.Check(device!.GetId(out var id));
            var client = Wasapi.Activate<IAudioClient>(device);
            // AUTOCONVERTPCM makes Windows resample to 48 kHz float stereo, so the
            // pipe format never changes when the user switches devices (a mono mic too).
            var flags = Wasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | Wasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            if (!_microphone) flags |= Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK;
            var capture = Start(client, flags);
            return (client, capture, new Source(id, 0));
        }
        finally
        {
            Wasapi.Release(device);
        }
    }

    private (IAudioClient, IAudioCaptureClient, Source) OpenProcess(uint pid)
    {
        var client = Wasapi.ActivateProcessLoopback(pid);
        // Process loopback is event-driven only; the event is signalled but the
        // 10 ms poll does the reading, same as the other sources.
        var capture = Start(client, Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK | Wasapi.AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
            Wasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | Wasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY);
        return (client, capture, new Source(null, pid));
    }

    private IAudioCaptureClient Start(IAudioClient client, uint flags)
    {
        var format = WaveFormatExtensible.FloatStereo(SampleRate);
        Wasapi.Check(client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED, flags, BufferDuration100ns, 0, format, 0));
        if ((flags & Wasapi.AUDCLNT_STREAMFLAGS_EVENTCALLBACK) != 0)
        {
            Wasapi.Check(client.SetEventHandle(_deviceEvent.SafeWaitHandle.DangerousGetHandle()));
        }
        var capture = Wasapi.GetService<IAudioCaptureClient>(client);
        Wasapi.Check(client.Start());
        return capture;
    }

    private string? DefaultDeviceId(IMMDeviceEnumerator enumerator)
    {
        var flow = _microphone ? Wasapi.eCapture : Wasapi.eRender;
        if (enumerator.GetDefaultAudioEndpoint(flow, Wasapi.eConsole, out var device) < 0) return null;
        try
        {
            return device!.GetId(out var id) < 0 ? null : id;
        }
        finally
        {
            Wasapi.Release(device);
        }
    }

    private unsafe bool Drain(IAudioCaptureClient capture)
    {
        var got = false;
        Wasapi.Check(capture.GetNextPacketSize(out var frames));
        while (frames > 0)
        {
            Wasapi.Check(capture.GetBuffer(out var data, out var count, out var flags, out _, out _));
            if (_muted || (flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
            {
                EnqueueSilence(count);
            }
            else
            {
                Enqueue(new ReadOnlySpan<byte>((void*)data, (int)count * BytesPerFrame), silence: false);
            }
            Wasapi.Check(capture.ReleaseBuffer(count));
            got = true;
            Wasapi.Check(capture.GetNextPacketSize(out frames));
        }
        return got;
    }

    /// <summary>One named pipe for one FFmpeg run.</summary>
    private sealed class PipeSession : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _task;

        public PipeSession(AudioCapture owner, string name, ILogger log)
        {
            // Zero-size buffers: a write completes only when FFmpeg reads, so the
            // first completed write tells us FFmpeg's input has really started.
            _pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 0);
            _task = Task.Run(async () =>
            {
                var buffer = new byte[SampleRate / 100 * BytesPerFrame * 4]; // 40 ms
                try
                {
                    await _pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    await _pipe.WriteAsync(new byte[BytesPerFrame], _cts.Token).ConfigureAwait(false);
                    owner.StartStreaming();
                    while (!_cts.IsCancellationRequested)
                    {
                        var n = owner.Take(buffer, 100);
                        if (n > 0) await _pipe.WriteAsync(buffer.AsMemory(0, n), _cts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
                {
                    log.Debug("Audio pipe closed: {Reason}", ex.GetType().Name);
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _task.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
            _pipe.Dispose();
            _cts.Dispose();
        }
    }
}
