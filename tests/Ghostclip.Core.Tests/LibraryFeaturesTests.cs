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

        // Shown as empty, never a crash.
        File.WriteAllText(file, "{ broken");
        Assert.Empty(LibraryStore.Read(file).Favorites);
    }

    // The favorites in it keep clips from the space limit: an unreadable file must not pass for "no favorites".
    [Fact]
    public void An_unreadable_library_file_is_never_taken_for_an_empty_one()
    {
        var file = Path.Combine(_dir, "library.json");
        Assert.True(LibraryStore.TryRead(file, out var missing));
        Assert.Empty(missing.Favorites);

        LibraryStore.Update(file, s => s.SetFavorite(@"C:\Clips\G\a.mp4", true));
        Assert.True(LibraryStore.TryRead(file, out var read));
        Assert.True(read.IsFavorite(@"C:\Clips\G\a.mp4"));

        File.WriteAllText(file, "{ broken");
        Assert.False(LibraryStore.TryRead(file, out _));

        // Writing over it would wipe whatever favorites it held.
        Assert.Throws<IOException>(() => LibraryStore.Update(file, s => s.SetFavorite(@"C:\Clips\G\b.mp4", true)));
        Assert.Equal("{ broken", File.ReadAllText(file));
    }

    // Moving the clips folder (D: to E:, or another folder in Settings) keeps each clip's game folder and name.
    [Fact]
    public void Favorites_and_bookmarks_follow_clips_to_a_moved_clips_folder()
    {
        var state = new LibraryState();
        state.SetFavorite(@"D:\Clips\G\G_2026-10-01_12-00-00.mp4", true);
        state.SetBookmarks(@"D:\Clips\G\G_2026-10-02_12-00-00.mp4", [12.5]);

        Assert.True(state.IsFavorite(@"E:\Videos\Ghostclip\G\G_2026-10-01_12-00-00.mp4"));
        Assert.False(state.IsFavorite(@"E:\Videos\Ghostclip\H\G_2026-10-01_12-00-00.mp4"));
        Assert.Equal([12.5], state.BookmarksFor(@"E:\Videos\Ghostclip\G\G_2026-10-02_12-00-00.mp4"));

        var moved = new ClipEntry(@"E:\Videos\Ghostclip\G\G_2026-10-01_12-00-00.mp4", "G/G_2026-10-01_12-00-00.mp4", "G",
            new DateTime(2026, 10, 1), 900 * 1024 * 1024, false, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = moved with { Path = @"E:\Videos\Ghostclip\G\G_2026-10-03_12-00-00.mp4", LastWriteUtc = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Empty(StorageLimit.SelectForDeletion(@"E:\Videos\Ghostclip", [moved, newer], state, 1024L * 1024 * 1024));

        // Unstarring it in its new place takes the old entry away too, and the lookup sees it at once.
        state.SetFavorite(@"E:\Videos\Ghostclip\G\G_2026-10-01_12-00-00.mp4", false);
        Assert.Empty(state.Favorites);
        Assert.False(state.IsFavorite(@"E:\Videos\Ghostclip\G\G_2026-10-01_12-00-00.mp4"));
    }

    // A copy with the same game folder and name (a backup folder inside the clips folder) shows as starred, but
    // deleting or renaming it must leave the original's star, or the space limit could take the original.
    [Fact]
    public void Deleting_or_renaming_a_copy_keeps_the_original_s_star()
    {
        const string original = @"C:\Clips\G\G_2026-10-01_12-00-00.mp4";
        const string copy = @"C:\Clips\Backup\G\G_2026-10-01_12-00-00.mp4";
        var state = new LibraryState();
        state.SetFavorite(original, true);
        Assert.True(state.IsFavorite(copy));

        state.Forget(copy);
        Assert.True(state.IsFavorite(original));

        state.Move(copy, @"C:\Clips\Backup\G\Ace.mp4");
        Assert.True(state.IsFavorite(original));
        Assert.True(state.IsFavorite(@"C:\Clips\Backup\G\Ace.mp4"));

        // Unstarring the original itself takes its own entry only.
        state.SetFavorite(original, false);
        Assert.False(state.IsFavorite(original));
        Assert.True(state.IsFavorite(@"C:\Clips\Backup\G\Ace.mp4"));
    }

    // The space limit reads favorites, then deletes: a star set in between must wait for the pass, not be lost to it.
    [Fact]
    public void A_favorite_set_during_a_locked_pass_waits_for_it()
    {
        var file = Path.Combine(_dir, "library.json");
        var order = new List<string>();
        var holding = new ManualResetEventSlim();
        var pass = new Thread(() => LibraryStore.Locked(() =>
        {
            holding.Set();
            Thread.Sleep(300);
            lock (order) order.Add("pass");
            // A write from inside the pass takes the same lock again.
            LibraryStore.Update(file, s => s.Forget(@"C:\Clips\G\gone.mp4"));
        }));
        pass.Start();
        holding.Wait(TestContext.Current.CancellationToken);

        LibraryStore.Update(file, s => s.SetFavorite(@"C:\Clips\G\a.mp4", true));
        lock (order) order.Add("favorite");
        pass.Join();

        Assert.Equal(["pass", "favorite"], order);
        Assert.True(LibraryStore.Read(file).IsFavorite(@"C:\Clips\G\a.mp4"));
    }

    // An antivirus or the indexer holding the file for a moment is not a broken file.
    [Fact]
    public void Reading_the_library_file_waits_out_a_moment_s_lock()
    {
        var file = Path.Combine(_dir, "library.json");
        LibraryStore.Update(file, s => s.SetFavorite(@"C:\Clips\G\a.mp4", true));
        var holder = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = new Thread(() =>
        {
            Thread.Sleep(200);
            holder.Dispose();
        });
        release.Start();

        Assert.True(LibraryStore.TryRead(file, out var state));
        release.Join();
        Assert.True(state.IsFavorite(@"C:\Clips\G\a.mp4"));
    }

    // The library window reading the file while the tray writes a session's bookmarks must not lose the write.
    [Fact]
    public void Writing_the_library_file_waits_out_a_reader()
    {
        var file = Path.Combine(_dir, "library.json");
        LibraryStore.Update(file, s => s.SetFavorite(@"C:\Clips\G\a.mp4", true));
        var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() =>
        {
            Thread.Sleep(150);
            reader.Dispose();
        });
        release.Start();

        LibraryStore.Update(file, s => s.SetBookmarks(@"C:\Clips\G\s.mp4", [9]));

        release.Join();
        Assert.Equal([9.0], LibraryStore.Read(file).BookmarksFor(@"C:\Clips\G\s.mp4"));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
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
