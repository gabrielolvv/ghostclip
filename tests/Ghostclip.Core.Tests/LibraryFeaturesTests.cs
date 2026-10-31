using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Favorites, renaming, screenshots, the Discord track and session bookmarks.
public sealed class LibraryFeaturesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Make(string relative, DateTime? lastWriteUtc = null)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        if (lastWriteUtc is { } t) File.SetLastWriteTimeUtc(path, t);
        return path;
    }

    [Fact]
    public void Screenshots_are_listed_with_the_clips_as_images()
    {
        Make(@"G\G_2026-10-30_10-00-00.mp4");
        var shot = Make(@"G\G_2026-10-30_10-00-05.jpg");
        Make(@"G\G_2026-10-30_10-00-06.jpg.part");
        Make(@"G\notes.txt");

        var clips = ClipLibrary.Scan(_dir);

        Assert.Equal(2, clips.Count);
        var image = Assert.Single(clips, c => c.Image);
        Assert.Equal(shot, image.Path);
        Assert.Equal(new DateTime(2026, 10, 30, 10, 0, 5), image.SavedLocal);
        Assert.False(image.Derived);
    }

    [Fact]
    public void Screenshots_are_named_like_clips()
    {
        var time = new DateTime(2026, 10, 30, 21, 5, 9);
        var taken = Path.Combine(_dir, "Valorant", "Valorant_2026-10-30_21-05-09.jpg");
        Assert.Equal(taken, ClipNaming.BuildPath(_dir, "Valorant", time, _ => false, ".jpg"));
        Assert.Equal(Path.Combine(_dir, "Valorant", "Valorant_2026-10-30_21-05-09_2.jpg"),
            ClipNaming.BuildPath(_dir, "Valorant", time, p => p == taken, ".jpg"));
    }

    [Fact]
    public void A_cut_off_screenshot_is_cleaned_up_like_a_clip()
    {
        var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        var shot = Make(@"G\G_2026-10-01_10-00-00.jpg.part", now.AddDays(-2));
        var foreign = Make(@"G\wallpaper.jpg.part", now.AddDays(-2));

        Assert.Equal(1, PartFile.CleanAbandoned(_dir, TimeSpan.FromHours(1), now));
        Assert.False(File.Exists(shot));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void Renaming_keeps_the_folder_and_extension()
    {
        var clip = Path.Combine(_dir, "G", "G_2026-10-30_10-00-00.mp4");
        Assert.Equal(Path.Combine(_dir, "G", "Ace clutch.mp4"), ClipNaming.RenamedPath(clip, "  Ace clutch ", _ => false));
        // Typing the extension too means the same.
        Assert.Equal(Path.Combine(_dir, "G", "Ace clutch.mp4"), ClipNaming.RenamedPath(clip, "Ace clutch.mp4", _ => false));
        Assert.Null(ClipNaming.RenamedPath(clip, "G_2026-10-30_10-00-00", _ => true));
        // Only the case changes: the same file on Windows, so not "taken".
        Assert.Equal(Path.Combine(_dir, "G", "g_2026-10-30_10-00-00.mp4"), ClipNaming.RenamedPath(clip, "g_2026-10-30_10-00-00", _ => true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("ends with a dot.")]
    [InlineData("CON")]
    public void Renaming_refuses_names_windows_cant_use(string name)
    {
        Assert.Throws<InvalidOperationException>(() => ClipNaming.RenamedPath(Path.Combine(_dir, "G", "c.mp4"), name, _ => false));
    }

    [Fact]
    public void Renaming_never_replaces_another_file()
    {
        var clip = Path.Combine(_dir, "G", "c.mp4");
        Assert.Throws<InvalidOperationException>(() => ClipNaming.RenamedPath(clip, "other", p => p.EndsWith("other.mp4", StringComparison.Ordinal)));
    }

    [Fact]
    public void Favorites_and_bookmarks_follow_the_file()
    {
        var state = new LibraryState();
        state.SetFavorite(@"C:\Clips\G\a.mp4", true);
        state.SetBookmarks(@"C:\Clips\G\a.mp4", [30, -1, 5]);

        Assert.True(state.IsFavorite(@"c:\clips\g\A.MP4"));
        Assert.Equal([5.0, 30.0], state.BookmarksFor(@"C:\Clips\G\a.mp4"));

        state.Move(@"C:\Clips\G\a.mp4", @"C:\Clips\G\Ace.mp4");
        Assert.False(state.IsFavorite(@"C:\Clips\G\a.mp4"));
        Assert.True(state.IsFavorite(@"C:\Clips\G\Ace.mp4"));
        Assert.Equal([5.0, 30.0], state.BookmarksFor(@"C:\Clips\G\Ace.mp4"));

        state.Forget(@"C:\Clips\G\Ace.mp4");
        Assert.Empty(state.Favorites);
        Assert.Empty(state.Bookmarks);

        // Set twice is still one entry.
        state.SetFavorite(@"C:\Clips\G\b.mp4", true);
        state.SetFavorite(@"C:\CLIPS\G\b.mp4", true);
        Assert.Single(state.Favorites);
    }

    [Fact]
    public void The_library_file_survives_a_round_trip_and_a_broken_file()
    {
        var file = Path.Combine(_dir, "library.json");
        Assert.Empty(LibraryStore.Read(file).Favorites);

        LibraryStore.Update(file, s => s.SetFavorite(@"C:\Clips\G\a.mp4", true));
        LibraryStore.Update(file, s => s.SetBookmarks(@"C:\Clips\G\s.mp4", [12.5]));

        var read = LibraryStore.Read(file);
        Assert.True(read.IsFavorite(@"C:\Clips\G\a.mp4"));
        Assert.Equal([12.5], read.BookmarksFor(@"C:\Clips\G\s.mp4"));

        // It only holds conveniences: unreadable means empty, never a crash.
        File.WriteAllText(file, "{ broken");
        Assert.Empty(LibraryStore.Read(file).Favorites);
    }

    [Fact]
    public void The_page_gets_favorite_image_and_bookmarks()
    {
        var entry = new ClipEntry(@"C:\Clips\G\s.mp4", "G/s.mp4", "G", new DateTime(2026, 10, 30), 10, false, DateTime.UtcNow);
        var state = new LibraryState();
        Assert.Null(ClipDto.From(entry, state).Bookmarks);

        state.SetFavorite(entry.Path, true);
        state.SetBookmarks(entry.Path, [3]);
        var dto = ClipDto.From(entry, state);
        Assert.True(dto.Favorite);
        Assert.Equal([3.0], dto.Bookmarks);
        Assert.True(ClipDto.From(entry with { Image = true }).Image);
    }

    [Fact]
    public void Discord_on_its_own_track_restarts_recording()
    {
        var a = new GhostclipConfig();
        Assert.False(a.SameCaptureAs(a with { DiscordTrack = true }));
        Assert.True(a.SameCaptureAs(a with { ScreenshotHotkey = "F8", SessionHotkey = "F7" }));
    }

    [Fact]
    public void Every_action_has_a_hotkey()
    {
        var config = new GhostclipConfig { ScreenshotHotkey = " ", SessionHotkey = "" }.Normalize();
        Assert.Equal(["Alt+F10", "Alt+F11", "Alt+F9"], config.Hotkeys);
        // The list is for the app, not a setting of its own in config.json.
        Assert.DoesNotContain("\"hotkeys\"", JsonSerializer.Serialize(config, CoreJson.Default.GhostclipConfig));
    }

    [Fact]
    public void A_screenshot_is_one_frame_of_the_recorded_monitor()
    {
        var args = FfmpegArgs.Screenshot(1, @"C:\c\G\shot.jpg.part");
        Assert.Contains("ddagrab=output_idx=1:framerate=30,hwdownload,format=bgra,format=yuvj444p", args);
        Assert.Equal("1", args[args.ToList().IndexOf("-frames:v") + 1]);
        Assert.Equal("mjpeg", args[args.ToList().IndexOf("-c:v") + 1]);
        Assert.Equal(@"C:\c\G\shot.jpg.part", args[^1]);
    }
}
