using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The tray's "Copy last clip", and --copy-last for Stream Deck: the newest save, ready to paste in Discord.
public sealed class CopyLastClipTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ghostclip-last-" + Guid.NewGuid().ToString("N"));

    private string Make(string game, string name)
    {
        var path = Path.Combine(_dir, game, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    [Fact]
    public void The_newest_save_is_the_last_clip()
    {
        Make("Elden Ring", "Elden Ring_2026-11-15_20-00-00.mp4");
        var newest = Make("Desktop", "Desktop_2026-11-16_10-00-00.mp4");
        Assert.Equal(newest, ClipLibrary.LastClip(ClipLibrary.Scan(_dir))?.Path);
    }

    [Fact]
    public void Copies_and_pictures_made_after_it_are_not_the_last_clip()
    {
        var clip = Make("Desktop", "Desktop_2026-11-16_10-00-00.mp4");
        Make("Desktop", "Desktop_2026-11-16_10-00-00_trim.mp4");
        Make("Desktop", "Desktop_2026-11-16_10-00-00_discord.mp4");
        Make("Desktop", "Desktop_2026-11-16_11-00-00.jpg");
        Make("Desktop", "Desktop_2026-11-16_10-00-00_frame.jpg");
        Assert.Equal(clip, ClipLibrary.LastClip(ClipLibrary.Scan(_dir))?.Path);
    }

    [Fact]
    public void No_clips_means_no_last_clip()
    {
        Assert.Null(ClipLibrary.LastClip(ClipLibrary.Scan(_dir)));
        Make("Desktop", "Desktop_2026-11-16_11-00-00.jpg");
        Assert.Null(ClipLibrary.LastClip(ClipLibrary.Scan(_dir)));
    }

    [Fact]
    public void Copy_last_has_its_own_argument()
    {
        Assert.Equal(TrayCommand.CopyLast, TrayCommands.Parse("--copy-last"));
        Assert.Equal("--copy-last", TrayCommands.Argument(TrayCommand.CopyLast));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
