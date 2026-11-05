using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// What deleting by age takes. Same care as the space limit: the clips folder can be Videos or Downloads.
public class ClipAgeTests
{
    private const string Root = @"C:\Clips";
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static ClipEntry Clip(string relative, int daysOld) =>
        new(Path.Combine(Root, relative), relative.Replace('\\', '/'), relative.Split('\\')[0],
            Now.AddDays(-daysOld).ToLocalTime(), 1024, false, Now.AddDays(-daysOld));

    private static List<string> Old(IReadOnlyList<ClipEntry> clips, int days, LibraryState? state = null) =>
        [.. StorageLimit.SelectOlderThan(Root, clips, state ?? new LibraryState(), days, Now).Select(c => Path.GetFileName(c.Path))];

    [Fact]
    public void Off_takes_nothing()
    {
        var clips = new[] { Clip(@"G\G_2026-01-01_12-00-00.mp4", 300), Clip(@"G\G_2026-10-30_12-00-00.mp4", 1) };
        Assert.Empty(Old(clips, 0));
        Assert.Empty(Old(clips, -5));
    }

    [Fact]
    public void Only_what_is_past_the_age_goes_oldest_first()
    {
        var clips = new[]
        {
            Clip(@"G\G_2026-10-30_12-00-00.mp4", 1),
            Clip(@"G\G_2026-08-01_12-00-00.mp4", 90),
            Clip(@"H\H_2026-07-01_12-00-00.jpg", 120),
            Clip(@"G\G_2026-10-10_12-00-00.mp4", 21),
        };
        Assert.Equal(["H_2026-07-01_12-00-00.jpg", "G_2026-08-01_12-00-00.mp4"], Old(clips, 30));
    }

    [Fact]
    public void Exactly_the_age_stays()
    {
        var clips = new[] { Clip(@"G\G_2026-10-01_12-00-00.mp4", 30), Clip(@"G\G_2026-10-30_12-00-00.mp4", 1) };
        Assert.Empty(Old(clips, 30));
    }

    [Fact]
    public void Favorites_stay()
    {
        var old = Clip(@"G\G_2026-01-01_12-00-00.mp4", 300);
        var state = new LibraryState();
        state.SetFavorite(old.Path, true);
        Assert.Empty(Old([old, Clip(@"G\G_2026-10-30_12-00-00.mp4", 1)], 30, state));
    }

    [Theory]
    [InlineData(@"G\My best play.mp4")]                       // Renamed.
    [InlineData(@"Downloads\video_2026-01-01_12-00-00.mp4")]  // Another program's, with a date in its name.
    [InlineData(@"G_2026-01-01_12-00-00.mp4")]                // Not in a game's folder.
    [InlineData(@"G\Sub\G_2026-01-01_12-00-00.mp4")]          // Deeper than one.
    public void What_Ghostclip_did_not_name_stays(string relative)
    {
        Assert.Empty(Old([Clip(relative, 300), Clip(@"G\G_2026-10-30_12-00-00.mp4", 1)], 30));
    }

    [Fact]
    public void The_newest_stays_even_after_a_long_break()
    {
        var clips = new[] { Clip(@"G\G_2026-01-01_12-00-00.mp4", 300), Clip(@"G\G_2026-02-01_12-00-00.mp4", 270) };
        Assert.Equal(["G_2026-01-01_12-00-00.mp4"], Old(clips, 30));
    }

    [Fact]
    public void A_copy_made_today_of_an_old_clip_is_new()
    {
        var clips = new[] { Clip(@"G\G_2026-01-01_12-00-00_trim.mp4", 2), Clip(@"G\G_2026-10-30_12-00-00.mp4", 1) };
        Assert.Empty(Old(clips, 30));
    }

    [Fact]
    public void A_file_dated_in_the_future_counts_as_written_now()
    {
        var clips = new[] { Clip(@"G\G_2026-01-01_12-00-00.mp4", -400), Clip(@"G\G_2026-02-01_12-00-00.mp4", 300) };
        Assert.Equal(["G_2026-02-01_12-00-00.mp4"], Old(clips, 30));
    }

    [Fact]
    public void It_is_off_until_chosen_and_stays_in_range()
    {
        Assert.Equal(0, new GhostclipConfig().MaxClipAgeDays);
        Assert.Equal(0, new GhostclipConfig { MaxClipAgeDays = -5 }.Normalize().MaxClipAgeDays);
        Assert.Equal(36500, new GhostclipConfig { MaxClipAgeDays = 999999 }.Normalize().MaxClipAgeDays);
        Assert.True(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { MaxClipAgeDays = 30 }));
    }
}
