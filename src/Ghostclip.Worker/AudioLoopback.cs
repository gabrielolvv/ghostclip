using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Ghostclip.Core;
using Ghostclip.Worker.Native;
using Serilog;

namespace Ghostclip.Worker;

/// <summary>
/// Captures what the default playback device plays (WASAPI loopback) and feeds
/// it to FFmpeg as raw 48 kHz float stereo over a named pipe.
///
/// Loopback delivers nothing while the system is silent. A raw PCM input that
/// stops would stall FFmpeg's muxer and with it the video, so the gaps are
/// filled with silence against a QueryPerformanceCounter clock: the stream
/// always advances at real time.
/// </summary>
internal sealed class AudioLoopback : IDisposable
{
    private const int SampleRate = FfmpegArgs.AudioSampleRate;
    private const int BytesPerFrame = FfmpegArgs.AudioBytesPerFrame;
    private const long BufferDuration100ns = 1_000_000; // 100 ms device buffer
    private const int MaxSilenceGapFrames = SampleRate * 30 / 1000;

    private readonly ILogger _log;
    private readonly Thread _thread;
    private readonly object _sync = new();
    // Two seconds between capture and pipe: absorbs FFmpeg stalls without dropping.
    private readonly byte[] _ring = new byte[SampleRate * BytesPerFrame * 2];
    private readonly AutoResetEvent _dataReady = new(false);
    private int _ringStart;
    private int _ringCount;
    private bool _streaming;
    private long _clockStart;
    private long _framesQueued;
    private long _overflowBytes;
    private volatile bool _stop;
    private volatile bool _deviceOpen;
    private PipeSession? _session;
    private string? _lastError;

    public AudioLoopback(ILogger log)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "audio-loopback", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>True while a playback device is open. Silence is still produced without one.</summary>
    public bool DeviceOpen => _deviceOpen;

    /// <summary>QPC ticks of the first sample FFmpeg read in this session; 0 until then.</summary>
    public long StreamStartTicks => Volatile.Read(ref _streamStartTicks);

    private long _streamStartTicks;

    /// <summary>Creates the pipe for a new FFmpeg run and returns its path.</summary>
    public string BeginSession()
    {
        EndSession();
        var name = $"ghostclip-audio-{Environment.ProcessId}-{Guid.NewGuid():N}";
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
            _log.Warning("Audio pipe stalled; dropped {Ms} ms of audio", dropped / BytesPerFrame * 1000 / SampleRate);
        }
    }

    private void Run()
    {
        Wasapi.InitializeThread();
        IMMDeviceEnumerator? enumerator = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        string? deviceId = null;
        long retryAt = 0;
        long nextDeviceCheck = 0;

        while (!_stop)
        {
            var now = Stopwatch.GetTimestamp();
            try
            {
                enumerator ??= Wasapi.CreateEnumerator();
                if (client is null && now >= retryAt)
                {
                    (client, capture, deviceId) = Open(enumerator);
                    _deviceOpen = true;
                    _lastError = null;
                    _log.Information("Capturing system audio from {Device}", deviceId);
                }
                if (client is not null && now >= nextDeviceCheck)
                {
                    // Polled once a second: cheaper and simpler than a COM callback.
                    nextDeviceCheck = now + Stopwatch.Frequency;
                    if (DefaultDeviceId(enumerator) != deviceId)
                    {
                        _log.Information("Default playback device changed; switching");
                        Close(ref client, ref capture);
                    }
                }
                var got = capture is not null && Drain(capture);
                PadToClock(got);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Close(ref client, ref capture);
                retryAt = now + Stopwatch.Frequency / 2;
                if (_lastError != ex.Message)
                {
                    _lastError = ex.Message;
                    _log.Warning("System audio unavailable ({Error}); recording silence until a device appears", ex.Message);
                }
                PadToClock(gotPackets: false);
            }
            Thread.Sleep(10);
        }

        Close(ref client, ref capture);
        Wasapi.Release(enumerator);
    }

    private void Close(ref IAudioClient? client, ref IAudioCaptureClient? capture)
    {
        if (client is not null)
        {
            client.Stop();
        }
        Wasapi.Release(capture);
        Wasapi.Release(client);
        capture = null;
        client = null;
        _deviceOpen = false;
    }

    private static (IAudioClient, IAudioCaptureClient, string?) Open(IMMDeviceEnumerator enumerator)
    {
        Wasapi.Check(enumerator.GetDefaultAudioEndpoint(Wasapi.eRender, Wasapi.eConsole, out var device));
        try
        {
            Wasapi.Check(device!.GetId(out var id));
            var client = Wasapi.Activate<IAudioClient>(device);
            // AUTOCONVERTPCM makes Windows resample to 48 kHz float stereo, so the
            // pipe format never changes when the user switches devices.
            var format = WaveFormatExtensible.FloatStereo(SampleRate);
            Wasapi.Check(client.Initialize(
                Wasapi.AUDCLNT_SHAREMODE_SHARED,
                Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK | Wasapi.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | Wasapi.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                BufferDuration100ns, 0, format, 0));
            var capture = Wasapi.GetService<IAudioCaptureClient>(client);
            Wasapi.Check(client.Start());
            return (client, capture, id);
        }
        finally
        {
            Wasapi.Release(device);
        }
    }

    private static string? DefaultDeviceId(IMMDeviceEnumerator enumerator)
    {
        if (enumerator.GetDefaultAudioEndpoint(Wasapi.eRender, Wasapi.eConsole, out var device) < 0) return null;
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
            if ((flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
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

        public PipeSession(AudioLoopback owner, string name, ILogger log)
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
