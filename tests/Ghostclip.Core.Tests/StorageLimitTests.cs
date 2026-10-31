using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// What the space limit deletes. The clips folder can be Videos or Downloads, so most of these are about what it must not touch.
public class StorageLimitTests
{
    private const string Root = @"C:\Clips";
    private const long MB = 1024 * 1024;

    private static ClipEntry Clip(string relative, int day, long mb) =>
        new(Path.Combine(Root, relative), relative.Replace('\\', '/'), relative.Split('\\')[0],
            new DateTime(2026, 10, day, 12, 0, 0), mb * MB, false, DateTime.UtcNow);

    private static long Limit(long mb) => mb * MB;

    [Fact]
    public void No_limit_or_room_to_spare_deletes_nothing()
    {
        var clips = new[] { Clip(@"G\G_2026-10-01_12-00-00.mp4", 1, 500), Clip(@"G\G_2026-10-02_12-00-00.mp4", 2, 500) };
        Assert.Empty(StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), 0));
        Assert.Empty(StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), Limit(1000)));
    }

    [Fact]
    public void The_oldest_go_first_and_only_as_many_as_needed()
    {
        var clips = new[]
        {
            Clip(@"G\G_2026-10-03_12-00-00.mp4", 3, 300),
            Clip(@"G\G_2026-10-01_12-00-00.mp4", 1, 300),
            Clip(@"H\H_2026-10-02_12-00-00.jpg", 2, 300),
            Clip(@"G\G_2026-10-04_12-00-00.mp4", 4, 300),
        };
        var deleted = StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), Limit(700));
        Assert.Equal([@"C:\Clips\G\G_2026-10-01_12-00-00.mp4", @"C:\Clips\H\H_2026-10-02_12-00-00.jpg"], deleted.Select(c => c.Path));
    }

    [Fact]
    public void Favorites_stay_and_still_count()
    {
        var old = Clip(@"G\G_2026-10-01_12-00-00.mp4", 1, 600);
        var clips = new[] { old, Clip(@"G\G_2026-10-02_12-00-00.mp4", 2, 300), Clip(@"G\G_2026-10-03_12-00-00.mp4", 3, 300) };
        var state = new LibraryState();
        state.SetFavorite(old.Path, true);

        // 1200 MB against 900: the favorite would free most, but the next oldest goes instead.
        var deleted = StorageLimit.SelectForDeletion(Root, clips, state, Limit(900));
        Assert.Equal([@"C:\Clips\G\G_2026-10-02_12-00-00.mp4"], deleted.Select(c => c.Path));
    }

    [Fact]
    public void Renamed_clips_and_other_files_are_never_deleted()
    {
        var clips = new[]
        {
            Clip(@"G\Ace clutch.mp4", 1, 500),                       // renamed: theirs to keep
            Clip(@"Holiday.mp4", 1, 5000),                           // the folder's own video, not in a game's folder
            Clip(@"Series\S1\S1_2026-10-01_12-00-00.mp4", 1, 5000),  // deeper: not Ghostclip's layout
            Clip(@"G\G_2026-10-02_12-00-00.mp4", 2, 500),
            Clip(@"G\G_2026-10-03_12-00-00.mp4", 3, 500),
        };
        // Only files in a game's folder count: 1500 MB, not 11.5 GB.
        Assert.Equal(1500 * MB, StorageLimit.Used(Root, clips));
        var deleted = StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), Limit(600));
        Assert.Equal([@"C:\Clips\G\G_2026-10-02_12-00-00.mp4"], deleted.Select(c => c.Path));
    }

    [Fact]
    public void The_newest_is_kept_even_over_the_limit()
    {
        var clips = new[] { Clip(@"G\G_2026-10-01_12-00-00.mp4", 1, 300), Clip(@"G\G_2026-10-02_12-00-00.mp4", 2, 2000) };
        var deleted = StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), Limit(1000));
        Assert.Equal([@"C:\Clips\G\G_2026-10-01_12-00-00.mp4"], deleted.Select(c => c.Path));
    }

    [Fact]
    public void The_limit_is_off_by_default_and_never_negative()
    {
        Assert.Equal(0, new GhostclipConfig().StorageLimitGB);
        Assert.Equal(0, new GhostclipConfig { StorageLimitGB = -5 }.Normalize().StorageLimitGB);
        Assert.True(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { StorageLimitGB = 50 }));
    }
}
