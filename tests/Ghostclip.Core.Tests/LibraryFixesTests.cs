using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class LibraryFixesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ProcessResult Ran(string stdout = "", string stderr = "", int exit = 0, bool timedOut = false) =>
        new(exit, stdout, stderr, TimeSpan.FromSeconds(1), timedOut);

    // Derived clips (trim, compress) are written beside their final name and moved in when complete.

    [Fact]
    public void Part_file_is_moved_into_place_when_complete()
    {
        var output = Path.Combine(_dir, "a_trim.mp4");
        File.WriteAllText(PartFile.For(output), "x");
        PartFile.Commit(output);
        Assert.True(File.Exists(output));
        Assert.False(File.Exists(PartFile.For(output)));
        Assert.Equal(output, Assert.Single(ClipLibrary.Scan(_dir)).Path);
    }

    [Fact]
    public void Part_file_never_replaces_an_existing_clip()
    {
        var output = Path.Combine(_dir, "a_trim.mp4");
        File.WriteAllText(output, "old");
        File.WriteAllText(PartFile.For(output), "new");
        Assert.Throws<IOException>(() => PartFile.Commit(output));
        Assert.Equal("old", File.ReadAllText(output));
    }

    [Fact]
    public void Discarding_a_missing_part_file_is_fine()
    {
        PartFile.Discard(Path.Combine(_dir, "nothing.mp4"));
    }

    // A probe that failed or timed out is never cached as the clip's meta.

    [Fact]
    public void Meta_needs_the_keyframe_list_to_have_finished()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ClipMedia.BuildMeta(600, 1080, 60, 1, "h264", Ran("0.0,K__\n2.0,K__\n", timedOut: true), Ran(stderr: "")));
        Assert.Throws<InvalidOperationException>(() =>
            ClipMedia.BuildMeta(600, 1080, 60, 1, "h264", Ran(exit: 1), Ran()));
        Assert.Throws<InvalidOperationException>(() =>
            ClipMedia.BuildMeta(600, 1080, 60, 1, "h264", Ran("0.1,___\n"), Ran()));
    }

    [Fact]
    public void Meta_needs_the_peaks_when_the_clip_has_audio()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ClipMedia.BuildMeta(60, 1080, 60, 2, "h264", Ran("0.0,K__\n"), Ran(exit: 1)));
        Assert.Throws<InvalidOperationException>(() =>
            ClipMedia.BuildMeta(60, 1080, 60, 2, "h264", Ran("0.0,K__\n"), null));
    }

    [Fact]
    public void Complete_probes_make_meta()
    {
        var meta = ClipMedia.BuildMeta(60, 1080, 59.9401, 2, "hevc", Ran("0.0,K__\n2.0,K__\n"),
            Ran(stderr: "lavfi.astats.Overall.Peak_level=0.000000\n"));
        Assert.Equal([0.0, 2.0], meta.Keyframes);
        Assert.Equal([1f], meta.Peaks);
        Assert.Equal(59.94, meta.Fps);
        Assert.Empty(ClipMedia.BuildMeta(60, 1080, 60, 0, "h264", Ran("0.0,K__\n"), null).Peaks);
    }

    // The page keys what it knows about a clip by the file's identity, not just its path.

    [Fact]
    public void Clip_version_changes_when_the_file_does()
    {
        var at = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        ClipEntry Entry(long bytes, DateTime written) => new(@"C:\c\G\a_trim.mp4", "G/a_trim.mp4", "G", at, bytes, true, written);
        var v = ClipDto.From(Entry(10, at)).Version;
        Assert.Equal(v, ClipDto.From(Entry(10, at)).Version);
        Assert.NotEqual(v, ClipDto.From(Entry(11, at)).Version);
        Assert.NotEqual(v, ClipDto.From(Entry(10, at.AddSeconds(1))).Version);
    }

    [Fact]
    public void Scan_reports_when_each_file_was_written()
    {
        var path = Path.Combine(_dir, "G", "G_2026-10-30_21-14-02.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        var written = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        Assert.Equal(written, Assert.Single(ClipLibrary.Scan(_dir)).LastWriteUtc);
    }

    // A clips folder that can't be used (unplugged drive, no permission) is reported, not thrown.

    [Fact]
    public void Unusable_clips_folder_is_reported()
    {
        var file = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(file, "x");
        Assert.False(ClipLibrary.TryPrepare(Path.Combine(file, "Clips"), out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Usable_clips_folder_is_created()
    {
        var root = Path.Combine(_dir, "Clips");
        Assert.True(ClipLibrary.TryPrepare(root, out var error));
        Assert.Null(error);
        Assert.True(Directory.Exists(root));
    }

    // The bridge only answers the app's own page.

    [Theory]
    [InlineData("https://app.ghostclip/index.html", true)]
    [InlineData("https://APP.ghostclip/", true)]
    [InlineData("file:///C:/Users/me/Videos/clip.mp4", false)]
    [InlineData("https://app.ghostclip.example.com/", false)]
    [InlineData("http://app.ghostclip/index.html", false)]
    [InlineData("https://clips.ghostclip/G/a.mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_app_page_counts(string? uri, bool app)
    {
        Assert.Equal(app, LibraryOrigin.IsApp(uri));
    }
}
