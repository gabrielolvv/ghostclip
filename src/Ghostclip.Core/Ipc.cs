using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghostclip.Core;

/// <summary>Message types on the host/worker pipe.</summary>
public static class IpcType
{
    /// <summary>Host → worker: the config to record with. Sent on connect and on every change.</summary>
    public const string Config = "config";

    /// <summary>Host → worker: stop recording and exit.</summary>
    public const string Shutdown = "shutdown";

    /// <summary>Host → worker: the foreground game changed. Drives game-only audio and per-game mic mute.</summary>
    public const string Foreground = "foreground";

    /// <summary>Worker → host: heartbeat, every second. Silence means the worker hung.</summary>
    public const string Status = "status";

    /// <summary>Worker → host: something the user should know about.</summary>
    public const string Error = "error";
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkerState>))]
public enum WorkerState { Starting, Probing, Recording, Restarting, Stopped }

public sealed record WorkerStatus
{
    public WorkerState State { get; set; }
    public string? Encoder { get; set; }
    public long Frames { get; set; }
    public double Fps { get; set; }
    public long Dropped { get; set; }
    public long Duplicated { get; set; }
    public bool Audio { get; set; }

    /// <summary>What the main audio track is recording right now ("system", "game: Elden Ring").</summary>
    public string? AudioRoute { get; set; }

    public bool Microphone { get; set; }
}

public sealed record IpcMessage
{
    public required string Type { get; init; }
    public GhostclipConfig? Config { get; set; }
    public WorkerStatus? Status { get; set; }
    public string? Message { get; set; }

    /// <summary>On a foreground message: the game's name and process id (0 for the desktop).</summary>
    public string? Game { get; set; }
    public uint ProcessId { get; set; }

    /// <summary>On an error: the worker can't record until the config changes.</summary>
    public bool Fatal { get; set; }
}

/// <summary>Newline-delimited JSON over a pipe. Writes are serialized; reads come from one loop.</summary>
public sealed class IpcChannel : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public IpcChannel(Stream stream)
    {
        _stream = stream;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public async Task SendAsync(IpcMessage message, CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(message, CoreJson.Default.IpcMessage);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>The next message, or null when the other side closed the pipe.</summary>
    public async Task<IpcMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return null;
            if (line.Length == 0) continue;
            return JsonSerializer.Deserialize(line, CoreJson.Default.IpcMessage)
                ?? throw new JsonException("Empty IPC message.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        try
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The peer is gone (crashed or exited): there is nothing left to flush to.
        }
        _writeLock.Dispose();
    }
}
