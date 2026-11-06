using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The frame a clip shows on its card, when people pick one. It lives in the library's file and the cache, never in the video.
public class ClipCoverTests
{
    private const string Clip = @"C:\Clips\G\G_2026-10-30_21-14-02.mp4";

    [Fact]
    public void A_clip_has_none_until_chosen()
    {
        Assert.Null(new LibraryState().CoverFor(Clip));
    }

    [Fact]
    public void Choosing_again_replaces_it()
    {
        var state = new LibraryState();
        state.SetCover(Clip, 12.5);
        state.SetCover(Clip, 30);
        Assert.Equal(30, state.CoverFor(Clip));
        Assert.Single(state.Covers);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_time_that_makes_no_sense_is_not_kept(double at)
    {
        var state = new LibraryState();
        state.SetCover(Clip, at);
        Assert.Null(state.CoverFor(Clip));
    }

    [Fact]
    public void Renaming_carries_it_along()
    {
        var state = new LibraryState();
        state.SetCover(Clip, 12.5);
        state.Move(Clip, @"C:\Clips\G\Best play.mp4");
        Assert.Equal(12.5, state.CoverFor(@"C:\Clips\G\Best play.mp4"));
        Assert.Null(state.CoverFor(Clip));
    }

    [Fact]
    public void Deleting_takes_it_away()
    {
        var state = new LibraryState();
        state.SetCover(Clip, 12.5);
        state.Forget(Clip);
        Assert.Null(state.CoverFor(Clip));
    }

    [Fact]
    public void A_clips_folder_moved_keeps_it()
    {
        var state = new LibraryState();
        state.SetCover(Clip, 12.5);
        Assert.Equal(12.5, state.CoverFor(@"E:\Moved\G\G_2026-10-30_21-14-02.mp4"));
    }

    [Fact]
    public void It_survives_the_library_file()
    {
        var state = new LibraryState();
        state.SetCover(Clip, 12.5);
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(state, CoreJson.Default.LibraryState), CoreJson.Default.LibraryState);
        Assert.Equal(12.5, back!.CoverFor(Clip));
    }

    [Fact]
    public void A_library_file_from_before_covers_reads_fine()
    {
        Assert.Null(JsonSerializer.Deserialize("""{"favorites":[]}""", CoreJson.Default.LibraryState)!.CoverFor(Clip));
    }

    [Fact]
    public void The_automatic_thumbnail_keeps_its_name()
    {
        Assert.Equal("0123456789abcdef.jpg", ClipMedia.ThumbName("0123456789abcdef", null));
    }

    [Fact]
    public void A_cover_has_a_name_of_its_own_for_each_time_so_the_window_doesnt_show_an_old_one()
    {
        Assert.Equal("0123456789abcdef.12500.jpg", ClipMedia.ThumbName("0123456789abcdef", 12.5));
        Assert.NotEqual(ClipMedia.ThumbName("0123456789abcdef", 12.5), ClipMedia.ThumbName("0123456789abcdef", 30));
    }

    [Fact]
    public void The_name_uses_no_comma_whatever_the_language_of_Windows()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            Assert.Equal("0123456789abcdef.1500.jpg", ClipMedia.ThumbName("0123456789abcdef", 1.5));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void An_old_cover_is_pruned_with_the_rest_of_the_cache()
    {
        var dir = Directory.CreateTempSubdirectory("ghostclip-cover-").FullName;
        try
        {
            var cover = Path.Combine(dir, ClipMedia.ThumbName("0123456789abcdef", 12.5));
            File.WriteAllBytes(cover, [1]);
            File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddDays(-200));
            Assert.Equal(1, ClipMedia.PruneCache(dir, TimeSpan.FromDays(90), DateTime.UtcNow));
            Assert.False(File.Exists(cover));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
