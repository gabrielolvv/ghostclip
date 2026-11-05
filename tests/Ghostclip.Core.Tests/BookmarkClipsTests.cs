using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Clips cut from a session around its bookmarks. The clip the key saves ends at the press; these go on past it.
public class BookmarkClipsTests
{
    private static readonly double[] NoKeyframes = [];

    private static List<(double Start, double End)> Ranges(double[] bookmarks, double duration = 3600, double before = 60, double[]? keyframes = null) =>
        [.. BookmarkClips.Ranges(bookmarks, duration, before, keyframes ?? NoKeyframes)];

    [Fact]
    public void Each_goes_from_the_clip_length_before_to_15_seconds_after()
    {
        Assert.Equal([(940.0, 1015.0)], Ranges([1000]));
    }

    [Fact]
    public void Far_apart_they_make_a_clip_each_in_order()
    {
        Assert.Equal([(40.0, 115.0), (940.0, 1015.0)], Ranges([1000, 100]));
    }

    [Fact]
    public void Close_ones_make_one_clip()
    {
        Assert.Equal([(940.0, 1045.0)], Ranges([1000, 1030]));
    }

    [Fact]
    public void Touching_ones_make_one_clip()
    {
        Assert.Equal([(40.0, 190.0)], Ranges([100, 175]));
    }

    [Fact]
    public void A_chain_of_them_makes_one_clip()
    {
        Assert.Equal([(40.0, 265.0)], Ranges([100, 150, 200, 250]));
    }

    [Fact]
    public void The_same_one_twice_is_one()
    {
        Assert.Equal([(940.0, 1015.0)], Ranges([1000, 1000]));
    }

    [Fact]
    public void They_stay_inside_the_session()
    {
        Assert.Equal([(0.0, 25.0)], Ranges([10]));
        Assert.Equal([(3530.0, 3600.0)], Ranges([3590]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3601)]
    public void One_outside_the_session_is_left_out(double at)
    {
        Assert.Empty(Ranges([at]));
    }

    [Fact]
    public void None_make_nothing()
    {
        Assert.Empty(Ranges([]));
    }

    [Fact]
    public void The_start_goes_back_to_a_keyframe_since_the_cut_copies()
    {
        Assert.Equal([(938.0, 1015.0)], Ranges([1000], keyframes: [0, 936, 938, 940.5, 942]));
    }

    [Fact]
    public void The_clip_length_is_the_games()
    {
        Assert.Equal([(985.0, 1015.0)], Ranges([1000], before: 15));
    }
}
