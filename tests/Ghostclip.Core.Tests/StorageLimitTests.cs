using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// What the space limit deletes. The clips folder can be Videos or Downloads, so most of these are about what it must not touch.
public class StorageLimitTests
{
    private const string Root = @"C:\Clips";
    private const long MB = 1024 * 1024;

    // Written on the day in its name unless said otherwise: a session or a trim is written later than its name says.
    private static ClipEntry Clip(string relative, int day, long mb, int? writtenDay = null) =>
        new(Path.Combine(Root, relative), relative.Replace('\\', '/'), relative.Split('\\')[0],
            new DateTime(2026, 10, day, 12, 0, 0), mb * MB, false, new DateTime(2026, 10, writtenDay ?? day, 15, 0, 0, DateTimeKind.Utc));

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
        // Only the files Ghostclip named, in a game's folder, count: 1000 MB, not 12 GB.
        Assert.Equal(1000 * MB, StorageLimit.Used(Root, clips));
        var deleted = StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), Limit(100));
        Assert.Equal([@"C:\Clips\G\G_2026-10-02_12-00-00.mp4"], deleted.Select(c => c.Path));
    }

    [Fact]
    public void Another_program_s_files_with_a_date_in_the_name_are_never_counted_or_deleted()
    {
        // Downloads as the clips folder: Telegram Desktop saves video_<date>_<time>.mp4 in a folder of its own.
        var clips = new[]
        {
            Clip(@"Telegram Desktop\video_2026-10-01_12-00-00.mp4", 1, 4000),
            Clip(@"Telegram Desktop\photo_2026-10-01_12-00-00.jpg", 1, 10),
            Clip(@"G\Other_2026-10-01_12-00-00.mp4", 1, 4000),
            Clip(@"G\G_2026-10-02_12-00-00.mp4", 2, 500),
            Clip(@"G\G_2026-10-03_12-00-00.mp4", 3, 500),
        };
        Assert.Equal(1000 * MB, StorageLimit.Used(Root, clips));
        var deleted = StorageLimit.SelectForDeletion(Root, clips, new LibraryState(), Limit(600));
        Assert.Equal([@"C:\Clips\G\G_2026-10-02_12-00-00.mp4"], deleted.Select(c => c.Path));
    }

    [Fact]
    public void A_session_saved_just_now_is_the_newest_even_though_its_name_has_its_start_time()
    {
        // A session started on the 1st and saved on the 3rd, a clip saved during it on the 2nd.
        var session = Clip(@"G\G_2026-10-01_12-00-00.mp4", 1, 2000, writtenDay: 3);
        var duringSession = Clip(@"G\G_2026-10-02_12-00-00.mp4", 2, 100);
        var older = Clip(@"G\G_2026-09-30_12-00-00.mp4", 1, 100, writtenDay: 1);
        var deleted = StorageLimit.SelectForDeletion(Root, [session, duringSession, older], new LibraryState(), Limit(1000));
        Assert.Equal([older.Path, duringSession.Path], deleted.Select(c => c.Path));
    }

    [Fact]
    public void A_trim_made_today_of_an_old_clip_is_not_among_the_oldest()
    {
        var oldClip = Clip(@"G\G_2026-10-01_12-00-00.mp4", 1, 300);
        var newerClip = Clip(@"G\G_2026-10-05_12-00-00.mp4", 5, 300);
        var trimToday = Clip(@"G\G_2026-10-01_12-00-00_trim.mp4", 1, 100, writtenDay: 10);
        var newest = Clip(@"G\G_2026-10-09_12-00-00.mp4", 9, 300);
        var deleted = StorageLimit.SelectForDeletion(Root, [oldClip, newerClip, trimToday, newest], new LibraryState(), Limit(500));
        Assert.Equal([oldClip.Path, newerClip.Path], deleted.Select(c => c.Path));
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
