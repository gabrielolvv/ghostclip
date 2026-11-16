using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class MoveGameTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ghostclip-move-tests");

    private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void A_saved_clip_takes_the_new_game_in_its_folder_and_name()
    {
        var moved = ClipNaming.MovedPath(Root, At("Desktop", "Desktop_2026-11-16_10-00-00.mp4"), "Elden Ring", _ => false);
        Assert.Equal(At("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4"), moved);
    }

    [Theory]
    [InlineData("Desktop_2026-11-16_10-00-00_trim.mp4", "Elden Ring_2026-11-16_10-00-00_trim.mp4")]
    [InlineData("Desktop_2026-11-16_10-00-00_2_discord_3.mp4", "Elden Ring_2026-11-16_10-00-00_2_discord_3.mp4")]
    [InlineData("Desktop_2026-11-16_10-00-00.jpg", "Elden Ring_2026-11-16_10-00-00.jpg")]
    [InlineData("Desktop_2026-11-16_10-00-00_frame.jpg", "Elden Ring_2026-11-16_10-00-00_frame.jpg")]
    public void Copies_and_pictures_keep_their_suffix_and_extension(string from, string to)
    {
        var moved = ClipNaming.MovedPath(Root, At("Desktop", from), "Elden Ring", _ => false);
        Assert.Equal(At("Elden Ring", to), moved);
        Assert.True(ClipLibrary.IsGhostclipName("Elden Ring", Path.GetFileNameWithoutExtension(moved!)));
        Assert.Equal(ClipLibrary.TimeFromName(from), ClipLibrary.TimeFromName(moved!));
    }

    [Fact]
    public void A_name_people_gave_stays_as_it_is()
    {
        var moved = ClipNaming.MovedPath(Root, At("Desktop", "Ace clutch.mp4"), "Elden Ring", _ => false);
        Assert.Equal(At("Elden Ring", "Ace clutch.mp4"), moved);
    }

    [Fact]
    public void The_game_is_made_safe_for_a_folder()
    {
        var moved = ClipNaming.MovedPath(Root, At("Desktop", "Desktop_2026-11-16_10-00-00.mp4"), "  Half:Life  ", _ => false);
        Assert.Equal(At("Half_Life", "Half_Life_2026-11-16_10-00-00.mp4"), moved);
    }

    [Theory]
    [InlineData("Desktop")]
    [InlineData("desktop")]
    [InlineData("  Desktop ")]
    public void The_same_game_moves_nothing(string game) =>
        Assert.Null(ClipNaming.MovedPath(Root, At("Desktop", "Desktop_2026-11-16_10-00-00.mp4"), game, _ => true));

    [Fact]
    public void A_name_taken_in_the_new_folder_gets_a_number()
    {
        var taken = At("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4");
        var moved = ClipNaming.MovedPath(Root, At("Desktop", "Desktop_2026-11-16_10-00-00.mp4"), "Elden Ring", p => p == taken);
        Assert.Equal(At("Elden Ring", "Elden Ring_2026-11-16_10-00-00_2.mp4"), moved);
    }

    [Fact]
    public void A_clip_loose_in_the_clips_folder_counts_as_the_desktop()
    {
        var moved = ClipNaming.MovedPath(Root, At("Desktop_2026-11-16_10-00-00.mp4"), "Elden Ring", _ => false);
        Assert.Equal(At("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4"), moved);
    }

    [Fact]
    public void A_clip_in_a_subfolder_goes_to_the_top_of_the_new_game()
    {
        var moved = ClipNaming.MovedPath(Root, At("Desktop", "old", "Desktop_2026-11-16_10-00-00.mp4"), "Elden Ring", _ => false);
        Assert.Equal(At("Elden Ring", "Elden Ring_2026-11-16_10-00-00.mp4"), moved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_game_is_refused(string? game) =>
        Assert.Throws<InvalidOperationException>(() =>
            ClipNaming.MovedPath(Root, At("Desktop", "Desktop_2026-11-16_10-00-00.mp4"), game, _ => false));
}
