using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class PolishTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // At 60 fps a keyframe sits at 8.233333 s. Rounded to 8.233 the seek would land just before it
    // and the cut would start at the previous keyframe, 2 s earlier than the timeline showed.
    [Fact]
    public void Trim_start_keeps_the_keyframe_time_exactly()
    {
        var args = FfmpegArgs.Trim("in.mp4", "out.mp4", 8.233333, 18).ToList();
        Assert.Equal("8.233333", args[args.IndexOf("-ss") + 1]);
    }

    // The library reopens where it was, unless that place is gone (a monitor unplugged, the resolution lowered).
    [Fact]
    public void Remembered_window_is_used_only_where_it_can_be_seen()
    {
        System.Drawing.Rectangle[] screens = [new(0, 0, 1920, 1040), new(1920, 0, 1280, 1000)];
        Assert.True(new WindowBounds(100, 100, 1280, 800, false).FitsOn(screens));
        Assert.True(new WindowBounds(2000, 50, 1000, 700, true).FitsOn(screens));
        Assert.False(new WindowBounds(4000, 100, 1280, 800, false).FitsOn(screens));
        Assert.False(new WindowBounds(-1250, 100, 1280, 800, false).FitsOn(screens));
        Assert.False(new WindowBounds(100, 100, 200, 100, false).FitsOn(screens));
    }

    [Fact]
    public void Remembered_window_survives_a_round_trip()
    {
        var path = Path.Combine(_dir, "library-window.json");
        Assert.Null(WindowBounds.Read(path));
        var bounds = new WindowBounds(10, 20, 1300, 820, true);
        bounds.Write(path);
        Assert.Equal(bounds, WindowBounds.Read(path));
        File.WriteAllText(path, "nope");
        Assert.Null(WindowBounds.Read(path));
    }

    [Fact]
    public async Task Saving_settings_waits_out_a_reader_holding_the_file()
    {
        var path = Path.Combine(_dir, "config.json");
        ConfigStore.Save(path, new GhostclipConfig());
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // A thread of its own lets go of the file: a pool task's delay could run late on a busy
        // runner, past Save's retries, which then failed the release build.
        var release = new Thread(() =>
        {
            Thread.Sleep(150);
            reader.Dispose();
        });
        release.Start();

        // On another thread, so the reader can let go while Save is retrying.
        await Task.Run(() => ConfigStore.Save(path, new GhostclipConfig { ClipSeconds = 45 }), TestContext.Current.CancellationToken);

        release.Join();
        Assert.True(ConfigStore.TryRead(path, out var config, out _));
        Assert.Equal(45, config!.ClipSeconds);
        Assert.False(File.Exists(path + ".tmp"));
    }
}
