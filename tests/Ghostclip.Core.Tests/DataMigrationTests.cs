using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class DataMigrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Make(params string[] parts)
    {
        var path = Path.Combine([_dir, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, parts[^1]);
        return path;
    }

    [Fact]
    public void Old_data_folder_keeps_config_and_pending_clips_and_goes_away()
    {
        Make("Ghostclip", "Data", "config.json");
        Make("Ghostclip", "Data", "Pending", "20261030-210000-000", "seg_000001.ts");
        Make("Ghostclip", "Data", "Buffer", "seg_000002.ts");
        var paths = new GhostclipPaths(Path.Combine(_dir, "GhostclipData"));

        paths.MigrateFrom(Path.Combine(_dir, "Ghostclip", "Data"), deleteOld: true);

        Assert.Equal("config.json", File.ReadAllText(paths.ConfigFile));
        Assert.True(File.Exists(Path.Combine(paths.PendingDir, "20261030-210000-000", "seg_000001.ts")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "Ghostclip", "Data")));
    }

    [Fact]
    public void Install_root_only_gives_up_its_config()
    {
        Make("Ghostclip", "config.json");
        Make("Ghostclip", "current", "Ghostclip.exe");
        var paths = new GhostclipPaths(Path.Combine(_dir, "GhostclipData"));

        paths.MigrateFrom(Path.Combine(_dir, "Ghostclip"), deleteOld: false);

        Assert.True(File.Exists(paths.ConfigFile));
        Assert.True(File.Exists(Path.Combine(_dir, "Ghostclip", "current", "Ghostclip.exe")));
    }

    [Fact]
    public void Existing_config_wins_and_missing_old_folder_is_fine()
    {
        Make("GhostclipData", "config.json");
        Make("Ghostclip", "Data", "config.json");
        var paths = new GhostclipPaths(Path.Combine(_dir, "GhostclipData"));
        File.WriteAllText(paths.ConfigFile, "new");

        paths.MigrateFrom(Path.Combine(_dir, "Ghostclip", "Data"), deleteOld: true);
        paths.MigrateFrom(Path.Combine(_dir, "Nowhere"), deleteOld: true);

        Assert.Equal("new", File.ReadAllText(paths.ConfigFile));
    }
}
