using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class SettingsPageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static WorkerStatus Recording(double fps, string encoder = "h264_qsv (direct)") =>
        new() { State = WorkerState.Recording, Encoder = encoder, Fps = fps, Audio = true, Frames = 1234 };

    [Fact]
    public void Status_keeps_only_what_the_badge_shows()
    {
        Assert.Equal(new RecorderStatus(42, WorkerState.Recording, "QSV", 60, true), RecorderStatus.From(42, Recording(59.94)));
        // Frame counts and jitter around 60 fps are not a change worth writing to disk.
        Assert.Equal(RecorderStatus.From(42, Recording(59.94)), RecorderStatus.From(42, Recording(60.04) with { Frames = 99999 }));
    }

    [Theory]
    [InlineData("hevc_nvenc (nv12)", "NVENC")]
    [InlineData("h264_amf (direct)", "AMF")]
    [InlineData("h264_qsv", "QSV")]
    [InlineData(null, null)]
    public void Encoder_is_named_by_its_family(string? encoder, string? family)
    {
        Assert.Equal(family, RecorderStatus.From(1, Recording(60, encoder!)).Encoder);
    }

    [Fact]
    public void Status_file_round_trips_and_tolerates_missing_or_broken_files()
    {
        var path = Path.Combine(_dir, "status.json");
        Assert.Null(StatusFile.Read(path));
        var status = new RecorderStatus(42, WorkerState.WaitingForDesktop, null, 0, false);
        StatusFile.Write(path, status);
        Assert.Equal(status, StatusFile.Read(path));
        Assert.Contains("\"state\": \"WaitingForDesktop\"", File.ReadAllText(path));
        File.WriteAllText(path, "{ broken");
        Assert.Null(StatusFile.Read(path));
        StatusFile.Delete(path);
        Assert.False(File.Exists(path));
        StatusFile.Delete(path);
    }

    [Fact]
    public void Status_file_lives_with_the_data()
    {
        Assert.Equal(Path.Combine(_dir, "status.json"), new GhostclipPaths(_dir).StatusFile);
    }

    [Fact]
    public void Live_reload_reads_without_touching_the_file()
    {
        var path = Path.Combine(_dir, "config.json");
        Assert.False(ConfigStore.TryRead(path, out _, out var missing));
        Assert.NotNull(missing);
        Assert.False(File.Exists(path));

        // A file half-way through a hand edit is left alone, not replaced with defaults.
        File.WriteAllText(path, """{ "clipSeconds": 90, """);
        Assert.False(ConfigStore.TryRead(path, out _, out var broken));
        Assert.NotNull(broken);
        Assert.Equal("""{ "clipSeconds": 90, """, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".invalid"));

        File.WriteAllText(path, """{ "clipSeconds": 5000, "hotkey": "Alt+F9" }""");
        Assert.True(ConfigStore.TryRead(path, out var config, out var error));
        Assert.Null(error);
        Assert.Equal(600, config!.ClipSeconds);
        Assert.Equal("Alt+F9", config.Hotkey);
    }

    [Fact]
    public void Save_request_carries_the_settings_from_the_page()
    {
        const string json = """
            {"type":"saveSettings","confirmed":true,"config":{"hotkey":"Alt+F9","clipSeconds":90,"codec":"Hevc",
             "audioSource":"GameOnly","profiles":[{"game":"Elden Ring","muteMicrophone":true}]}}
            """;
        var request = JsonSerializer.Deserialize(json, LibraryJson.Default.LibraryRequest)!;
        Assert.True(request.Confirmed);
        Assert.Equal("Alt+F9", request.Config!.Hotkey);
        Assert.Equal(90, request.Config.ClipSeconds);
        Assert.Equal(VideoCodec.Hevc, request.Config.Codec);
        Assert.Equal(AudioSourceMode.GameOnly, request.Config.AudioSource);
        Assert.True(Assert.Single(request.Config.Profiles).MuteMicrophone);
    }

    [Fact]
    public void Settings_event_sends_enums_as_names()
    {
        var message = new LibraryEvent("settings", Config: new GhostclipConfig { Encoder = EncoderKind.Qsv },
            Encoders: ["Qsv"], Games: ["Elden Ring"], Status: new RecorderStatus(1, WorkerState.Recording, "QSV", 60, true));
        var json = JsonSerializer.Serialize(message, LibraryJson.Default.LibraryEvent);
        Assert.Contains("\"encoder\":\"Qsv\"", json);
        Assert.Contains("\"state\":\"Recording\"", json);
        Assert.Contains("\"games\":[\"Elden Ring\"]", json);
    }
}
