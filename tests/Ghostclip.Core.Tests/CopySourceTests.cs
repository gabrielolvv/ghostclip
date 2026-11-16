using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A copy leads back to the clip it was made from: the player shows "from ..." and opens it.
public class CopySourceTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "ghostclip-source", "Elden Ring");

    private static string At(string name) => Path.Combine(Dir, name);

    private static string? Source(string name, params string[] existing) =>
        ClipLibrary.SourceOf(At(name), p => existing.Select(At).Contains(p, StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("Elden Ring_2026-11-16_10-00-00_trim.mp4")]
    [InlineData("Elden Ring_2026-11-16_10-00-00_discord.mp4")]
    [InlineData("Elden Ring_2026-11-16_10-00-00_vertical_2.mp4")]
    [InlineData("Elden Ring_2026-11-16_10-00-00_slow.mp4")]
    [InlineData("Elden Ring_2026-11-16_10-00-00_gif.gif")]
    [InlineData("Elden Ring_2026-11-16_10-00-00_frame_3.jpg")]
    public void A_copy_comes_from_the_clip_its_name_starts_with(string copy) =>
        Assert.Equal(At("Elden Ring_2026-11-16_10-00-00.mp4"), Source(copy, "Elden Ring_2026-11-16_10-00-00.mp4"));

    [Fact]
    public void A_copy_of_a_copy_comes_from_that_copy()
    {
        Assert.Equal(At("Elden Ring_2026-11-16_10-00-00_trim.mp4"),
            Source("Elden Ring_2026-11-16_10-00-00_trim_discord.mp4", "Elden Ring_2026-11-16_10-00-00.mp4", "Elden Ring_2026-11-16_10-00-00_trim.mp4"));
    }

    [Fact]
    public void A_save_with_a_number_is_not_a_copy() =>
        Assert.Null(Source("Elden Ring_2026-11-16_10-00-00_2.mp4", "Elden Ring_2026-11-16_10-00-00.mp4"));

    [Fact]
    public void A_joined_clip_comes_from_several_and_leads_to_none() =>
        Assert.Null(Source("Elden Ring_2026-11-16_10-00-00_joined.mp4", "Elden Ring_2026-11-16_10-00-00.mp4"));

    [Fact]
    public void A_copy_whose_clip_is_gone_leads_nowhere() =>
        Assert.Null(Source("Elden Ring_2026-11-16_10-00-00_trim.mp4"));

    [Fact]
    public void A_name_people_gave_a_copy_still_leads_back_when_it_ends_like_one() =>
        Assert.Equal(At("Ace clutch.mp4"), Source("Ace clutch_discord.mp4", "Ace clutch.mp4"));

    [Fact]
    public void The_listing_carries_where_a_copy_came_from()
    {
        var entry = new ClipEntry(At("x_trim.mp4"), "x_trim.mp4", "Elden Ring", DateTime.Now, 1, true, DateTime.UtcNow);
        Assert.Equal(At("x.mp4"), ClipDto.From(entry, null, At("x.mp4")).Source);
        Assert.Null(ClipDto.From(entry).Source);
    }
}
