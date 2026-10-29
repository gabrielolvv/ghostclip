using System.IO.Pipes;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class ConfigAndIpcTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_config_creates_defaults()
    {
        var path = Path.Combine(_dir, "config.json");
        var config = ConfigStore.Load(path, out var problem);
        Assert.Null(problem);
        Assert.Equal(new GhostclipConfig(), config);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Config_round_trips_with_enums_as_text()
    {
        var path = Path.Combine(_dir, "config.json");
        var config = new GhostclipConfig { Encoder = EncoderKind.Qsv, Codec = VideoCodec.Hevc, ClipSeconds = 30 };
        ConfigStore.Save(path, config);
        Assert.Contains("\"encoder\": \"Qsv\"", File.ReadAllText(path));
        Assert.Equal(config, ConfigStore.Load(path, out _));
    }

    [Fact]
    public void Broken_config_falls_back_to_defaults_and_keeps_the_file()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ \"clipSeconds\": ");
        var config = ConfigStore.Load(path, out var problem);
        Assert.Equal(new GhostclipConfig(), config);
        Assert.NotNull(problem);
        Assert.True(File.Exists(path + ".invalid"));
    }

    [Fact]
    public void Missing_fields_keep_their_defaults()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{ "clipSeconds": 30 }""");
        var config = ConfigStore.Load(path, out _);
        Assert.Equal(new GhostclipConfig { ClipSeconds = 30 }, config);
        Assert.True(config.SystemAudio);
        Assert.Equal(60, config.Fps);
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{ "clipSeconds": 5, "fps": 1000, "bitrateMbps": 0, "outputHeight": 100, "hotkey": " " }""");
        var config = ConfigStore.Load(path, out _);
        Assert.Equal(10, config.ClipSeconds);
        Assert.Equal(144, config.Fps);
        Assert.Equal(2, config.BitrateMbps);
        Assert.Equal(360, config.OutputHeight);
        Assert.Equal("Alt+F10", config.Hotkey);
    }

    [Fact]
    public void Clip_length_change_does_not_restart_capture()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { ClipSeconds = 90, Hotkey = "F9" }));
        Assert.False(a.SameCaptureAs(a with { BitrateMbps = 30 }));
    }

    [Fact]
    public async Task Messages_cross_a_named_pipe()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = "ghostclip-test-" + Guid.NewGuid().ToString("N");
        // Non-zero buffers: with zero, a write only completes once the peer reads.
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(ct), client.ConnectAsync(ct));

        var host = new IpcChannel(server);
        var worker = new IpcChannel(client);
        await host.SendAsync(new IpcMessage { Type = IpcType.Config, Config = new GhostclipConfig { Fps = 30 } }, ct);
        await worker.SendAsync(new IpcMessage { Type = IpcType.Status, Status = new WorkerStatus { State = WorkerState.Recording, Frames = 42 } }, ct);

        var toWorker = await worker.ReceiveAsync(ct);
        var toHost = await host.ReceiveAsync(ct);
        Assert.Equal(30, toWorker?.Config?.Fps);
        Assert.Equal(WorkerState.Recording, toHost?.Status?.State);
        Assert.Equal(42, toHost?.Status?.Frames);

        await worker.DisposeAsync();
        Assert.Null(await host.ReceiveAsync(ct));
        await host.DisposeAsync();
    }

    [Fact]
    public void Probe_cache_prefers_dedicated_gpus_then_direct_input()
    {
        var cache = new ProbeCache("id", VideoCodec.H264, 0, DateTime.UtcNow,
        [
            new(EncoderKind.Qsv, EncoderInput.Nv12, true, 1, null),
            new(EncoderKind.Qsv, EncoderInput.Direct, true, 1, null),
            new(EncoderKind.Nvenc, EncoderInput.Direct, false, 1, "no driver"),
        ]);
        Assert.Equal((EncoderKind.Qsv, EncoderInput.Direct), (cache.Choose(EncoderKind.Auto, EncoderInput.Auto)!.Encoder, cache.Choose(EncoderKind.Auto, EncoderInput.Auto)!.Input));
        Assert.Equal(EncoderInput.Nv12, cache.Choose(EncoderKind.Qsv, EncoderInput.Nv12)!.Input);
        Assert.Null(cache.Choose(EncoderKind.Nvenc, EncoderInput.Auto));
    }
}
