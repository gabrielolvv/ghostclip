using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The copies for Discord and for Shorts mix one audio track: which ones go in is up to the person.
public class ExportTracksTests
{
    private static readonly CompressionPlan Plan = new(8000, 128, 0, 0);

    private static List<IReadOnlyList<string>> Both(int total, IReadOnlyList<int>? keep) =>
    [
        FfmpegArgs.Compress("in.mp4", "out.mp4", Plan, "h264_nvenc", total, keep),
        FfmpegArgs.Vertical("in.mp4", "out.mp4", "h264_nvenc", total, 60, keep),
    ];

    [Fact]
    public void Without_a_choice_every_track_is_mixed()
    {
        Assert.All(Both(3, null), args => Assert.Contains("[0:a:0][0:a:1][0:a:2]amix=inputs=3:duration=first:normalize=0[a]", args));
    }

    [Fact]
    public void The_tracks_chosen_are_mixed_and_the_rest_stay_out()
    {
        Assert.All(Both(3, [0, 2]), args => Assert.Contains("[0:a:0][0:a:2]amix=inputs=2:duration=first:normalize=0[a]", args));
    }

    [Fact]
    public void One_track_chosen_is_copied_in_without_a_mix()
    {
        Assert.All(Both(3, [1]), args =>
        {
            Assert.Contains("0:a:1?", args);
            Assert.DoesNotContain("-filter_complex", args);
        });
    }

    [Fact]
    public void No_track_chosen_makes_a_silent_copy()
    {
        Assert.All(Both(2, []), args =>
        {
            Assert.Contains("-an", args);
            Assert.DoesNotContain("-filter_complex", args);
            Assert.DoesNotContain("0:a:0?", args);
        });
    }

    [Theory]
    [InlineData(new[] { 0, 0, 1 }, new[] { 0, 1 })]
    [InlineData(new[] { 1, 0 }, new[] { 0, 1 })]
    [InlineData(new[] { 0, 5, -1 }, new[] { 0 })]
    public void Repeated_or_impossible_tracks_are_ignored(int[] keep, int[] used)
    {
        Assert.Equal(used, ClipMedia.TracksToKeep(2, keep));
    }

    [Fact]
    public void The_page_says_which_tracks()
    {
        var request = JsonSerializer.Deserialize("""{"type":"vertical","path":"a.mp4","tracks":[0,2]}""", LibraryJson.Default.LibraryRequest)!;
        Assert.Equal([0, 2], request.Tracks);
        Assert.Null(JsonSerializer.Deserialize("""{"type":"vertical","path":"a.mp4"}""", LibraryJson.Default.LibraryRequest)!.Tracks);
    }
}
