using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class CleanupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Make(string relative, DateTime writtenUtc)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    // A trim or save that was cut off (the window closed, the PC shut down) leaves a .part nobody will finish.
    [Fact]
    public void Abandoned_part_files_are_removed_and_recent_ones_kept()
    {
        var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        var old = Make(@"G\G_2026-10-29_10-00-00_trim.mp4.part", now.AddDays(-2));
        var fresh = Make(@"G\G_2026-10-30_20-59-00.mp4.part", now.AddMinutes(-1));
        var clip = Make(@"G\G_2026-10-29_10-00-00.mp4", now.AddDays(-2));

        Assert.Equal(1, PartFile.CleanAbandoned(_dir, TimeSpan.FromHours(1), now));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(clip));
        Assert.Equal(0, PartFile.CleanAbandoned(Path.Combine(_dir, "missing"), TimeSpan.FromHours(1), now));
    }

    // The clips folder can be Downloads or Videos: a paused download of someone else's (yt-dlp, a browser)
    // named like a .part must survive, and so must anything outside the per-game folders.
    [Fact]
    public void Only_ghostclips_own_part_files_are_touched()
    {
        var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        var download = Make(@"some video.mp4.part", now.AddDays(-5));
        var nested = Make(@"Downloads\Series\G_2026-10-01_10-00-00.mp4.part", now.AddDays(-5));
        var foreign = Make(@"G\trailer.mp4.part", now.AddDays(-5));
        var ours = Make(@"G\G_2026-10-01_10-00-00_discord_2.mp4.part", now.AddDays(-5));

        Assert.Equal(1, PartFile.CleanAbandoned(_dir, TimeSpan.FromHours(1), now));

        Assert.True(File.Exists(download));
        Assert.True(File.Exists(nested));
        Assert.True(File.Exists(foreign));
        Assert.False(File.Exists(ours));
    }

    // Right after FFmpeg is stopped, the file can still be held for a moment (its handle closing, antivirus).
    [Fact]
    public async Task Discarding_waits_out_a_brief_lock()
    {
        var output = Path.Combine(_dir, "a_discord.mp4");
        File.WriteAllText(PartFile.For(output), "x");
        var holder = new FileStream(PartFile.For(output), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await holder.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        await Task.Run(() => PartFile.Discard(output), TestContext.Current.CancellationToken);

        await release;
        Assert.False(File.Exists(PartFile.For(output)));
    }

    // Thumbnails of clips deleted long ago would pile up forever.
    [Fact]
    public void Cached_details_unused_for_months_are_removed()
    {
        var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        var stale = Make(@"Thumbs\0123456789abcdef.jpg", now.AddDays(-120));
        var staleMeta = Make(@"Thumbs\0123456789abcdef.json", now.AddDays(-120));
        var used = Make(@"Thumbs\fedcba9876543210.jpg", now.AddDays(-3));
        var other = Make(@"Thumbs\readme.txt", now.AddDays(-400));

        Assert.Equal(2, ClipMedia.PruneCache(Path.Combine(_dir, "Thumbs"), TimeSpan.FromDays(90), now));

        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(staleMeta));
        Assert.True(File.Exists(used));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Reading_cached_details_counts_as_using_them()
    {
        var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        var meta = Make(@"Thumbs\0123456789abcdef.json", now.AddDays(-120));
        ClipMedia.Touch(meta, now);
        Assert.Equal(0, ClipMedia.PruneCache(Path.Combine(_dir, "Thumbs"), TimeSpan.FromDays(90), now));
    }
}
