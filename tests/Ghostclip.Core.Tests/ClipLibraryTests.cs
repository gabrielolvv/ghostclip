using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class ClipLibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Scan_groups_by_game_folder_newest_first()
    {
        Make("Hades II", "Hades II_2026-10-29_19-02-00.mp4");
        Make("Elden Ring", "Elden Ring_2026-10-30_21-14-02.mp4");
        var loose = Make("loose.mp4");
        File.SetLastWriteTime(loose, new DateTime(2026, 10, 1, 12, 0, 0));

        var clips = ClipLibrary.Scan(_root);

        Assert.Equal(["Elden Ring", "Hades II", ClipNaming.FallbackGame], clips.Select(c => c.Game));
        Assert.Equal(new DateTime(2026, 10, 30, 21, 14, 2), clips[0].SavedLocal);
        Assert.Equal(new DateTime(2026, 10, 1, 12, 0, 0), clips[2].SavedLocal);
        Assert.Equal(1, clips[0].Bytes);
    }

    [Fact]
    public void Scan_ignores_part_files_and_other_extensions()
    {
        Make("G", "a.mp4.part");
        Make("G", "b.mkv");
        Make("G", "c.MP4");

        var clip = Assert.Single(ClipLibrary.Scan(_root));
        Assert.Equal("c.MP4", Path.GetFileName(clip.Path));
    }

    [Fact]
    public void Scan_of_missing_root_is_empty()
    {
        Assert.Empty(ClipLibrary.Scan(Path.Combine(_root, "nowhere")));
    }

    [Theory]
    [InlineData("G_2026-10-30_21-14-02.mp4", true)]
    [InlineData("Counter-Strike 2_2026-10-30_21-14-02_2_trim_3.mp4", true)]
    [InlineData("G_2026-10-30_21-14-02_discord.mp4", true)]
    [InlineData("clip.mp4", false)]
    public void Time_comes_from_the_file_name(string name, bool parsed)
    {
        var time = ClipLibrary.TimeFromName(name);
        Assert.Equal(parsed ? new DateTime(2026, 10, 30, 21, 14, 2) : (DateTime?)null, time);
    }

    [Theory]
    [InlineData("G_2026-10-30_21-14-02_trim.mp4", true)]
    [InlineData("G_2026-10-30_21-14-02_discord_2.mp4", true)]
    [InlineData("G_2026-10-30_21-14-02_2.mp4", false)]
    [InlineData("G_2026-10-30_21-14-02.mp4", false)]
    public void Trimmed_and_compressed_copies_are_marked(string name, bool derived)
    {
        Assert.Equal(derived, ClipLibrary.IsDerived(name));
    }

    [Theory]
    [InlineData(@"C:\c\G\a.mp4", true)]
    [InlineData(@"c:\C\a.mp4", true)]
    [InlineData(@"C:\clips2\a.mp4", false)]
    [InlineData(@"C:\c\..\x.mp4", false)]
    [InlineData(@"C:\c", false)]
    public void Only_paths_inside_the_root_are_accepted(string path, bool inside)
    {
        Assert.Equal(inside, ClipLibrary.IsInside(@"C:\c", path));
    }

    [Fact]
    public void Relative_url_escapes_each_segment()
    {
        var path = Make("Counter-Strike 2", "#1 100% é.mp4");
        Assert.Equal("Counter-Strike%202/%231%20100%25%20%C3%A9.mp4", ClipLibrary.ToRelativeUrl(_root, path));
        Assert.Equal("Counter-Strike%202/%231%20100%25%20%C3%A9.mp4", Assert.Single(ClipLibrary.Scan(_root)).RelativeUrl);
    }
}
