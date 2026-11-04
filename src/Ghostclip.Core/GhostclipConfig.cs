using System.Text.Json.Serialization;

namespace Ghostclip.Core;

[JsonConverter(typeof(JsonStringEnumConverter<VideoCodec>))]
public enum VideoCodec { H264, Hevc }

[JsonConverter(typeof(JsonStringEnumConverter<EncoderKind>))]
public enum EncoderKind { Auto, Nvenc, Amf, Qsv }

/// <summary>
/// How frames reach the encoder. Nv12 converts BGRA to NV12 in an explicit GPU
/// step first; Direct hands BGRA to the encoder and lets it convert. Both stay
/// on the GPU; which one costs fewer FPS depends on the driver. Memory copies
/// each frame to system memory and hands that BGRA to the encoder, which still
/// encodes on the GPU: the fallback for drivers that refuse the captured
/// texture (AMF on a Radeon RX 6600), NVENC and AMF only.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EncoderInput>))]
public enum EncoderInput { Auto, Nv12, Direct, Memory }

/// <summary>
/// How the screen is read. Desktop Duplication (ddagrab) is the default; Windows
/// Graphics Capture (gfxcapture) is the alternative, since Desktop Duplication can
/// take a game out of its fastest fullscreen path and cost it frames.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CaptureMethod>))]
public enum CaptureMethod { DesktopDuplication, GraphicsCapture }

[JsonConverter(typeof(JsonStringEnumConverter<GhostclipLogLevel>))]
public enum GhostclipLogLevel { Debug, Information, Warning, Error }

/// <summary>The language of Ghostclip's own texts. Auto follows Windows: Portuguese on a Portuguese Windows, English otherwise.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UiLanguage>))]
public enum UiLanguage { Auto, English, Portuguese }

/// <summary>What the main audio track records.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AudioSourceMode>))]
public enum AudioSourceMode
{
    /// <summary>Everything the default playback device plays.</summary>
    System,

    /// <summary>Only the foreground game and its child processes (process loopback). Discord and music stay out.</summary>
    GameOnly,
}

/// <summary>Per-game overrides. Null fields inherit the global setting.</summary>
public sealed record GameProfile
{
    /// <summary>The name Ghostclip shows for the game (the clip folder name).</summary>
    public string Game { get; set; } = "";
    public int? ClipSeconds { get; set; }
    public AudioSourceMode? AudioSource { get; set; }
    public bool? MuteMicrophone { get; set; }
}

/// <summary>A list with value equality, so configs compare by content.</summary>
public sealed class GameProfileList : List<GameProfile>
{
    public GameProfileList() { }

    public GameProfileList(IEnumerable<GameProfile> items) : base(items) { }

    public override bool Equals(object? obj) => obj is GameProfileList other && this.SequenceEqual(other);

    public override int GetHashCode() => Count;
}

// Settable rather than init-only: the JSON source generator sets every init-only
// property, so a field missing from a hand-edited config.json would become 0
// instead of keeping its default.
public sealed record GhostclipConfig
{
    /// <summary>Length of every segment, and of the GOP. Fixed: the save logic assumes it.</summary>
    public const int SegmentSeconds = 2;

    public string Hotkey { get; set; } = "Alt+F10";

    /// <summary>Saves a picture of the monitor being recorded.</summary>
    public string ScreenshotHotkey { get; set; } = "Alt+F11";

    /// <summary>Starts and stops a session recording; while one runs, the clip hotkey also adds a bookmark.</summary>
    public string SessionHotkey { get; set; } = "Alt+F9";

    /// <summary>Mutes and unmutes the microphone track while recording. Registered only while the microphone is on.</summary>
    public string MicMuteHotkey { get; set; } = "Alt+F8";

    /// <summary>Saves a clip of <see cref="ShortClipSeconds"/>. Empty, the default: it takes no key from other programs.</summary>
    public string ShortClipHotkey { get; set; } = "";

    /// <summary>The length of that clip, in every game: profiles change only <see cref="ClipSeconds"/>.</summary>
    public int ShortClipSeconds { get; set; } = 15;

    /// <summary>Stops recording and deletes the replay buffer until pressed again. Empty, the default: the tray menu still does it.</summary>
    public string PauseHotkey { get; set; } = "";

    public int ClipSeconds { get; set; } = 60;
    public int Fps { get; set; } = 60;
    public int BitrateMbps { get; set; } = 20;
    public VideoCodec Codec { get; set; } = VideoCodec.H264;
    public EncoderKind Encoder { get; set; } = EncoderKind.Auto;
    public EncoderInput Input { get; set; } = EncoderInput.Auto;

    /// <summary>0 keeps the monitor's resolution.</summary>
    public int OutputHeight { get; set; }

    /// <summary>
    /// Desktop Duplication output index (0 = first monitor of the adapter). Graphics Capture records the
    /// monitor behind the same output, so both methods record the same screen.
    /// </summary>
    public int Monitor { get; set; }

    public bool SystemAudio { get; set; } = true;

    /// <summary>System audio or only the game's. Switches live: no restart, buffer kept.</summary>
    public AudioSourceMode AudioSource { get; set; } = AudioSourceMode.System;

    /// <summary>Records the default microphone as a second audio track.</summary>
    public bool Microphone { get; set; }

    /// <summary>
    /// Records the Discord app on a track of its own and leaves it out of the
    /// system track, so a call can be muted or turned down after the fact.
    /// </summary>
    public bool DiscordTrack { get; set; }

    /// <summary>Size limit for "Compress for Discord" (10 MB on a free account).</summary>
    public int DiscordLimitMB { get; set; } = 10;

    /// <summary>
    /// Space the clips folder may take; past it the oldest clips and screenshots are
    /// deleted (<see cref="StorageLimit"/>). 0 keeps everything, the default: nothing
    /// is ever deleted unless people chose a limit.
    /// </summary>
    public int StorageLimitGB { get; set; }

    public GameProfileList Profiles { get; set; } = [];

    /// <summary>Shifts audio against video to fix a constant lip-sync offset.</summary>
    public int AudioOffsetMs { get; set; }

    /// <summary>QuickSync frame queue. 1 saves RAM; higher absorbs GPU stalls.</summary>
    public int QsvAsyncDepth { get; set; } = 1;

    /// <summary>How the screen is read (<see cref="CaptureMethod"/>). Changing it restarts recording.</summary>
    public CaptureMethod Capture { get; set; } = CaptureMethod.DesktopDuplication;

    /// <summary>The recorder runs below normal priority on the CPU and GPU (<see cref="RecorderPriority"/>). Applies live.</summary>
    public bool GamePriority { get; set; } = true;

    /// <summary>Null uses Videos\Ghostclip.</summary>
    public string? ClipsFolder { get; set; }

    /// <summary>Null searches next to the app, then tools\, then PATH.</summary>
    public string? FfmpegPath { get; set; }

    public bool ConfirmationSound { get; set; } = true;

    /// <summary>Starts Ghostclip at sign-in (installed copies only).</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Applies live to the tray and the library; never touches recording.</summary>
    public UiLanguage Language { get; set; } = UiLanguage.Auto;
    public GhostclipLogLevel LogLevel { get; set; } = GhostclipLogLevel.Information;

    public string ResolvedClipsFolder =>
        string.IsNullOrWhiteSpace(ClipsFolder) ? GhostclipPaths.DefaultClipsRoot : ClipsFolder;

    /// <summary>Clamps every value into a range the recorder can actually run with.</summary>
    public GhostclipConfig Normalize() => Clamped().WithDistinctHotkeys();

    private static readonly string[] SpareHotkeys = ["Alt+F11", "Alt+F9", "Alt+F12", "Alt+F8", "Ctrl+Alt+F11", "Ctrl+Alt+F9", "Ctrl+Alt+F12", "Ctrl+Alt+F8"];

    // A clip hotkey picked before screenshots, sessions and muting existed can be one of their defaults (Alt+F9,
    // Alt+F11, Alt+F8). The clip hotkey keeps its key; each other one, in order, moves to the first spare
    // nothing else uses. The short clip and pause hotkeys, the optional ones, go back to none instead: a key
    // nobody chose would take it from other programs.
    private GhostclipConfig WithDistinctHotkeys()
    {
        static bool Same(string a, string b) => HotkeyRules.Canonical(a) == HotkeyRules.Canonical(b);
        // Eight spares and at most five taken: there is always one.
        string Spare(params string[] taken) => SpareHotkeys.First(s => !taken.Any(t => Same(s, t)));
        var screenshot = Same(ScreenshotHotkey, Hotkey) ? Spare(Hotkey, SessionHotkey, MicMuteHotkey, ShortClipHotkey, PauseHotkey) : ScreenshotHotkey;
        var session = Same(SessionHotkey, Hotkey) || Same(SessionHotkey, screenshot) ? Spare(Hotkey, screenshot, MicMuteHotkey, ShortClipHotkey, PauseHotkey) : SessionHotkey;
        var mute = Same(MicMuteHotkey, Hotkey) || Same(MicMuteHotkey, screenshot) || Same(MicMuteHotkey, session)
            ? Spare(Hotkey, screenshot, session, ShortClipHotkey, PauseHotkey)
            : MicMuteHotkey;
        var shortClip = new[] { Hotkey, screenshot, session, mute }.Any(k => Same(k, ShortClipHotkey)) ? "" : ShortClipHotkey;
        var pause = new[] { Hotkey, screenshot, session, mute, shortClip }.Any(k => Same(k, PauseHotkey)) ? "" : PauseHotkey;
        return this with { ScreenshotHotkey = screenshot, SessionHotkey = session, MicMuteHotkey = mute, ShortClipHotkey = shortClip, PauseHotkey = pause };
    }

    private GhostclipConfig Clamped() => this with
    {
        // A common shortcut (Ctrl+C) saved before they were refused goes back to the default: it broke copying everywhere.
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) || HotkeyRules.TakesCommonShortcut(Hotkey) ? "Alt+F10" : Hotkey.Trim(),
        ScreenshotHotkey = string.IsNullOrWhiteSpace(ScreenshotHotkey) || HotkeyRules.TakesCommonShortcut(ScreenshotHotkey) ? "Alt+F11" : ScreenshotHotkey.Trim(),
        SessionHotkey = string.IsNullOrWhiteSpace(SessionHotkey) || HotkeyRules.TakesCommonShortcut(SessionHotkey) ? "Alt+F9" : SessionHotkey.Trim(),
        MicMuteHotkey = string.IsNullOrWhiteSpace(MicMuteHotkey) || HotkeyRules.TakesCommonShortcut(MicMuteHotkey) ? "Alt+F8" : MicMuteHotkey.Trim(),
        ShortClipHotkey = string.IsNullOrWhiteSpace(ShortClipHotkey) || HotkeyRules.TakesCommonShortcut(ShortClipHotkey) ? "" : ShortClipHotkey.Trim(),
        ShortClipSeconds = Math.Clamp(ShortClipSeconds, 10, 600),
        PauseHotkey = string.IsNullOrWhiteSpace(PauseHotkey) || HotkeyRules.TakesCommonShortcut(PauseHotkey) ? "" : PauseHotkey.Trim(),
        ClipSeconds = Math.Clamp(ClipSeconds, 10, 600),
        Fps = Math.Clamp(Fps, 24, 144),
        BitrateMbps = Math.Clamp(BitrateMbps, 2, 150),
        OutputHeight = OutputHeight <= 0 ? 0 : Math.Clamp(OutputHeight, 360, 4320),
        Monitor = Math.Max(0, Monitor),
        AudioOffsetMs = Math.Clamp(AudioOffsetMs, -1000, 1000),
        QsvAsyncDepth = Math.Clamp(QsvAsyncDepth, 1, 8),
        DiscordLimitMB = Math.Clamp(DiscordLimitMB, 1, 4096),
        StorageLimitGB = Math.Clamp(StorageLimitGB, 0, 100_000),
        Profiles = new GameProfileList((Profiles ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Game))
            .Select(p => p with
            {
                Game = p.Game.Trim(),
                ClipSeconds = p.ClipSeconds is { } s ? Math.Clamp(s, 10, 600) : null,
            })),
    };

    public GameProfile? ProfileFor(string? game) =>
        game is null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Game, game, StringComparison.OrdinalIgnoreCase));

    public int ClipSecondsFor(string? game) => ProfileFor(game)?.ClipSeconds ?? ClipSeconds;

    public AudioSourceMode AudioSourceFor(string? game) => ProfileFor(game)?.AudioSource ?? AudioSource;

    /// <summary>The game's profile or the mute hotkey: either one mutes the microphone track.</summary>
    public bool MicrophoneMutedFor(string? game, bool byHotkey) => byHotkey || (ProfileFor(game)?.MuteMicrophone ?? false);

    /// <summary>The save, screenshot, session, mute, short clip and pause hotkeys, in that order. The last two are empty until chosen.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Hotkeys => [Hotkey, ScreenshotHotkey, SessionHotkey, MicMuteHotkey, ShortClipHotkey, PauseHotkey];

    [JsonIgnore]
    public bool HasShortClipHotkey => !string.IsNullOrWhiteSpace(ShortClipHotkey);

    [JsonIgnore]
    public bool HasPauseHotkey => !string.IsNullOrWhiteSpace(PauseHotkey);

    /// <summary>The hotkeys that have a key, to check them; <see cref="Hotkeys"/> keeps each action's place.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ChosenHotkeys => [.. Hotkeys.Where(g => !string.IsNullOrWhiteSpace(g))];

    /// <summary>The longest clip any hotkey or profile can ask for: the buffer must hold that much.</summary>
    public int MaxClipSeconds => Profiles.Select(p => p.ClipSeconds ?? 0).Append(ClipSeconds).Append(HasShortClipHotkey ? ShortClipSeconds : 0).Max();

    /// <summary>
    /// True when the two configs produce the same encoded stream. A change here
    /// restarts capture and drops the buffer, since segments in two formats
    /// can't be joined by stream copy.
    /// </summary>
    public bool SameCaptureAs(GhostclipConfig other) =>
        Fps == other.Fps && BitrateMbps == other.BitrateMbps && Codec == other.Codec &&
        Encoder == other.Encoder && Input == other.Input && OutputHeight == other.OutputHeight &&
        Monitor == other.Monitor && SystemAudio == other.SystemAudio && Microphone == other.Microphone &&
        DiscordTrack == other.DiscordTrack && AudioOffsetMs == other.AudioOffsetMs && QsvAsyncDepth == other.QsvAsyncDepth &&
        FfmpegPath == other.FfmpegPath && Capture == other.Capture;
}
