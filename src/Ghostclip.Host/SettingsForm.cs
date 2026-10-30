using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;
using Ghostclip.Core;

namespace Ghostclip.Host;

/// <summary>
/// Settings window. Created when opened and disposed when closed, so it costs
/// nothing while you play.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly SettingsModel _model;

    public SettingsForm(GhostclipConfig config, Action<GhostclipConfig> save)
    {
        _model = SettingsModel.From(config);
        Text = $"Ghostclip {typeof(SettingsForm).Assembly.GetName().Version?.ToString(3)} settings";
        Icon = TrayIcons.Recording;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 600);
        MinimumSize = new Size(420, 400);

        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = _model,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true,
        };
        var note = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Changes to Video or Audio restart recording and clear the replay buffer.",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        var ok = new Button { Text = "Save", Dock = DockStyle.Right, Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Dock = DockStyle.Right, Width = 90, DialogResult = DialogResult.Cancel };
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8) };
        bar.Controls.Add(note);
        bar.Controls.Add(ok);
        bar.Controls.Add(cancel);
        Controls.Add(grid);
        Controls.Add(bar);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) => save(_model.ToConfig(config).Normalize());
    }
}

/// <summary>What the property grid shows. Mirrors <see cref="GhostclipConfig"/>.</summary>
internal sealed class SettingsModel
{
    [Category("1. Clips"), DisplayName("Hotkey"), Description("Saves a clip. Examples: Alt+F10, Ctrl+Shift+F9.")]
    public string Hotkey { get; set; } = "";

    [Category("1. Clips"), DisplayName("Clip length (s)"), Description("How many seconds before the hotkey go into the clip (10 to 600).")]
    public int ClipSeconds { get; set; }

    [Category("1. Clips"), DisplayName("Clips folder"), Description("Clips go into a subfolder per game. Empty uses Videos\\Ghostclip.")]
    [Editor(typeof(FolderNameEditor), typeof(UITypeEditor))]
    public string ClipsFolder { get; set; } = "";

    [Category("1. Clips"), DisplayName("Confirmation sound"), Description("Plays a short chime when a clip is saved.")]
    public bool ConfirmationSound { get; set; }

    [Category("4. Advanced"), DisplayName("Start with Windows"), Description("Starts Ghostclip in the tray when you sign in. Installed copies only.")]
    public bool StartWithWindows { get; set; }

    [Category("2. Video"), DisplayName("Frame rate"), Description("Capture rate. Capturing below the monitor's refresh rate costs less.")]
    public int Fps { get; set; }

    [Category("2. Video"), DisplayName("Bitrate (Mbps)"), Description("Constant bitrate. 20 Mbps at 1080p60 uses about 150 MB of disk for 60 s.")]
    public int BitrateMbps { get; set; }

    [Category("2. Video"), DisplayName("Codec"), Description("H.264 plays everywhere. HEVC looks better at the same bitrate but some players and sites don't take it.")]
    public VideoCodec Codec { get; set; }

    [Category("2. Video"), DisplayName("Encoder"), Description("Auto picks NVENC, AMF or QuickSync. A CPU encoder is never used.")]
    public EncoderKind Encoder { get; set; }

    [Category("2. Video"), DisplayName("Encoder input"), Description("Where BGRA to YUV conversion happens. Auto picks what was measured to cost least.")]
    public EncoderInput Input { get; set; }

    [Category("2. Video"), DisplayName("Output height"), Description("0 keeps the monitor's resolution. 720 scales down on the GPU.")]
    public int OutputHeight { get; set; }

    [Category("2. Video"), DisplayName("Monitor"), Description("0 is the first monitor of the GPU.")]
    public int Monitor { get; set; }

    [Category("2. Video"), DisplayName("QuickSync queue depth"), Description("1 uses the least RAM. Raise it if clips show dropped frames under heavy GPU load.")]
    public int QsvAsyncDepth { get; set; }

    [Category("3. Audio"), DisplayName("System audio"), Description("Records what plays on the default output device.")]
    public bool SystemAudio { get; set; }

    [Category("3. Audio"), DisplayName("Audio source"), Description("System records everything you hear. GameOnly records just the game (no Discord, no music). Switches without restarting.")]
    public AudioSourceMode AudioSource { get; set; }

    [Category("3. Audio"), DisplayName("Microphone"), Description("Records the default microphone as a separate track. Turning it on or off restarts recording.")]
    public bool Microphone { get; set; }

    [Category("3. Audio"), DisplayName("Audio offset (ms)"), Description("Positive delays audio against video. Use it to fix a constant lip-sync error.")]
    public int AudioOffsetMs { get; set; }

    [Category("1. Clips"), DisplayName("Discord size limit (MB)"), Description("\"Compress for Discord\" fits clips under this size. 10 MB on a free account.")]
    public int DiscordLimitMB { get; set; }

    [Category("5. Game profiles"), DisplayName("Profiles"), Description("Per-game overrides. The game name must match the clip folder name. Empty fields use the global setting.")]
    public List<GameProfileModel> Profiles { get; set; } = [];

    [Category("4. Advanced"), DisplayName("FFmpeg path"), Description("Empty uses the bundled FFmpeg.")]
    public string FfmpegPath { get; set; } = "";

    [Category("4. Advanced"), DisplayName("Log level"), Description("Logs are in %LOCALAPPDATA%\\GhostclipData\\Logs.")]
    public GhostclipLogLevel LogLevel { get; set; }

    public static SettingsModel From(GhostclipConfig c) => new()
    {
        Hotkey = c.Hotkey,
        ClipSeconds = c.ClipSeconds,
        ClipsFolder = c.ClipsFolder ?? "",
        ConfirmationSound = c.ConfirmationSound,
        StartWithWindows = c.StartWithWindows,
        Fps = c.Fps,
        BitrateMbps = c.BitrateMbps,
        Codec = c.Codec,
        Encoder = c.Encoder,
        Input = c.Input,
        OutputHeight = c.OutputHeight,
        Monitor = c.Monitor,
        QsvAsyncDepth = c.QsvAsyncDepth,
        SystemAudio = c.SystemAudio,
        AudioSource = c.AudioSource,
        Microphone = c.Microphone,
        AudioOffsetMs = c.AudioOffsetMs,
        DiscordLimitMB = c.DiscordLimitMB,
        Profiles = c.Profiles.Select(GameProfileModel.From).ToList(),
        FfmpegPath = c.FfmpegPath ?? "",
        LogLevel = c.LogLevel,
    };

    public GhostclipConfig ToConfig(GhostclipConfig current) => current with
    {
        Hotkey = Hotkey,
        ClipSeconds = ClipSeconds,
        ClipsFolder = string.IsNullOrWhiteSpace(ClipsFolder) ? null : ClipsFolder,
        ConfirmationSound = ConfirmationSound,
        StartWithWindows = StartWithWindows,
        Fps = Fps,
        BitrateMbps = BitrateMbps,
        Codec = Codec,
        Encoder = Encoder,
        Input = Input,
        OutputHeight = OutputHeight,
        Monitor = Monitor,
        QsvAsyncDepth = QsvAsyncDepth,
        SystemAudio = SystemAudio,
        AudioSource = AudioSource,
        Microphone = Microphone,
        AudioOffsetMs = AudioOffsetMs,
        DiscordLimitMB = DiscordLimitMB,
        Profiles = new GameProfileList(Profiles.Select(p => p.ToProfile())),
        FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath,
        LogLevel = LogLevel,
    };
}

/// <summary>One row in the profiles editor. Empty fields inherit the global setting.</summary>
internal sealed class GameProfileModel
{
    [DisplayName("Game"), Description("As shown in the tray menu and used for the clip folder.")]
    public string Game { get; set; } = "";

    [DisplayName("Clip length (s)"), Description("Empty uses the global clip length.")]
    public int? ClipSeconds { get; set; }

    [DisplayName("Audio source"), Description("Empty uses the global audio source.")]
    public AudioSourceMode? AudioSource { get; set; }

    [DisplayName("Mute microphone"), Description("Records silence on the microphone track while this game is in front.")]
    public bool? MuteMicrophone { get; set; }

    public static GameProfileModel From(GameProfile p) => new()
    {
        Game = p.Game,
        ClipSeconds = p.ClipSeconds,
        AudioSource = p.AudioSource,
        MuteMicrophone = p.MuteMicrophone,
    };

    public GameProfile ToProfile() => new()
    {
        Game = Game,
        ClipSeconds = ClipSeconds,
        AudioSource = AudioSource,
        MuteMicrophone = MuteMicrophone,
    };

    public override string ToString() => string.IsNullOrWhiteSpace(Game) ? "(new profile)" : Game;
}
