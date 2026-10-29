using System.Text.Json.Serialization;

namespace Ghostclip.Core;

[JsonConverter(typeof(JsonStringEnumConverter<VideoCodec>))]
public enum VideoCodec { H264, Hevc }

[JsonConverter(typeof(JsonStringEnumConverter<EncoderKind>))]
public enum EncoderKind { Auto, Nvenc, Amf, Qsv }

/// <summary>
/// How frames reach the encoder. Nv12 converts BGRA to NV12 in an explicit GPU
/// step first; Direct hands BGRA to the encoder and lets it convert. Both stay
/// on the GPU; which one costs fewer FPS depends on the driver.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EncoderInput>))]
public enum EncoderInput { Auto, Nv12, Direct }

[JsonConverter(typeof(JsonStringEnumConverter<GhostclipLogLevel>))]
public enum GhostclipLogLevel { Debug, Information, Warning, Error }

// Settable rather than init-only: the JSON source generator sets every init-only
// property, so a field missing from a hand-edited config.json would become 0
// instead of keeping its default.
public sealed record GhostclipConfig
{
    /// <summary>Length of every segment, and of the GOP. Fixed: the save logic assumes it.</summary>
    public const int SegmentSeconds = 2;

    public string Hotkey { get; set; } = "Alt+F10";
    public int ClipSeconds { get; set; } = 60;
    public int Fps { get; set; } = 60;
    public int BitrateMbps { get; set; } = 20;
    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public EncoderKind Encoder { get; set; } = EncoderKind.Auto;
    public EncoderInput Input { get; set; } = EncoderInput.Auto;

    /// <summary>0 keeps the monitor's resolution.</summary>
    public int OutputHeight { get; set; }

    /// <summary>Desktop Duplication output index (0 = first monitor of the adapter).</summary>
    public int Monitor { get; set; }

    public bool SystemAudio { get; set; } = true;

    /// <summary>Shifts audio against video to fix a constant lip-sync offset.</summary>
    public int AudioOffsetMs { get; set; }

    /// <summary>QuickSync frame queue. 1 saves RAM; higher absorbs GPU stalls.</summary>
    public int QsvAsyncDepth { get; set; } = 1;

    /// <summary>Null uses Videos\Ghostclip.</summary>
    public string? ClipsFolder { get; set; }

    /// <summary>Null searches next to the app, then tools\, then PATH.</summary>
    public string? FfmpegPath { get; set; }

    public bool ConfirmationSound { get; set; } = true;
    public GhostclipLogLevel LogLevel { get; set; } = GhostclipLogLevel.Information;

    public string ResolvedClipsFolder =>
        string.IsNullOrWhiteSpace(ClipsFolder) ? GhostclipPaths.DefaultClipsRoot : ClipsFolder;

    /// <summary>Clamps every value into a range the recorder can actually run with.</summary>
    public GhostclipConfig Normalize() => this with
    {
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "Alt+F10" : Hotkey.Trim(),
        ClipSeconds = Math.Clamp(ClipSeconds, 10, 600),
        Fps = Math.Clamp(Fps, 24, 144),
        BitrateMbps = Math.Clamp(BitrateMbps, 2, 150),
        OutputHeight = OutputHeight <= 0 ? 0 : Math.Clamp(OutputHeight, 360, 4320),
        Monitor = Math.Max(0, Monitor),
        AudioOffsetMs = Math.Clamp(AudioOffsetMs, -1000, 1000),
        QsvAsyncDepth = Math.Clamp(QsvAsyncDepth, 1, 8),
    };

    /// <summary>
    /// True when the two configs produce the same encoded stream. A change here
    /// restarts capture and drops the buffer, since segments in two formats
    /// can't be joined by stream copy.
    /// </summary>
    public bool SameCaptureAs(GhostclipConfig other) =>
        Fps == other.Fps && BitrateMbps == other.BitrateMbps && Codec == other.Codec &&
        Encoder == other.Encoder && Input == other.Input && OutputHeight == other.OutputHeight &&
        Monitor == other.Monitor && SystemAudio == other.SystemAudio &&
        AudioOffsetMs == other.AudioOffsetMs && QsvAsyncDepth == other.QsvAsyncDepth &&
        FfmpegPath == other.FfmpegPath;
}
