using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Several clips into one video without re-encoding: only clips recorded the same way can be joined by stream copy.
public class JoinClipsTests
{
    private static JoinSource Source(string path, string video = "h264 1920x1080 MD5:a", params string[] audio) =>
        new(path, video, audio.Length == 0 ? ["aac 48000 MD5:b"] : audio);

    [Fact]
    public void They_join_oldest_first_whatever_order_they_were_picked_in()
    {
        string[] picked = [@"B\B_2026-11-04_12-00-00.mp4", @"A\A_2026-11-03_09-00-00.mp4", @"A\A_2026-11-04_10-00-00_trim.mp4"];
        Assert.Equal([@"A\A_2026-11-03_09-00-00.mp4", @"A\A_2026-11-04_10-00-00_trim.mp4", @"B\B_2026-11-04_12-00-00.mp4"], ClipJoiner.InOrder(picked));
    }

    [Fact]
    public void A_clip_without_a_time_in_its_name_keeps_a_stable_place()
    {
        string[] picked = [@"A\renamed.mp4", @"A\A_2026-11-03_09-00-00.mp4", @"A\another.mp4"];
        Assert.Equal([@"A\another.mp4", @"A\renamed.mp4", @"A\A_2026-11-03_09-00-00.mp4"], ClipJoiner.InOrder(picked));
    }

    [Fact]
    public void Clips_recorded_the_same_way_can_be_joined()
    {
        Assert.Null(ClipJoiner.FirstMismatch([Source("a"), Source("b"), Source("c")]));
    }

    [Fact]
    public void Another_video_format_can_t_be_joined()
    {
        // Other resolution, encoder or settings: a player would stumble where the formats change.
        Assert.Equal("b", ClipJoiner.FirstMismatch([Source("a"), Source("b", "h264 2560x1440 MD5:a"), Source("c", "hevc 1920x1080 MD5:c")])?.Path);
    }

    [Fact]
    public void A_different_set_of_audio_tracks_can_t_be_joined()
    {
        Assert.Equal("b", ClipJoiner.FirstMismatch([Source("a"), Source("b", audio: ["aac 48000 MD5:b", "aac 48000 MD5:b"])])?.Path);
        Assert.Equal("b", ClipJoiner.FirstMismatch([Source("a"), Source("b", audio: ["aac 44100 MD5:z"])])?.Path);
    }

    [Fact]
    public void The_arguments_copy_every_track_from_the_list()
    {
        var args = FfmpegArgs.Join("list.ffconcat", "out.mp4").ToList();
        Assert.Equal("concat", args[args.IndexOf("-f") + 1]);
        Assert.Equal("list.ffconcat", args[args.IndexOf("-i") + 1]);
        Assert.Equal("0", args[args.IndexOf("-map") + 1]);
        Assert.Equal("copy", args[args.IndexOf("-c") + 1]);
        Assert.Equal("out.mp4", args[^1]);
        Assert.DoesNotContain("-vf", args);
    }

    [Theory]
    [InlineData("G_2026-10-30_21-14-02_joined")]
    [InlineData("G_2026-10-30_21-14-02_joined_2")]
    [InlineData("G_2026-10-30_21-14-02_trim_joined")]
    public void The_result_counts_as_one_of_ghostclip_s_own_files(string stem)
    {
        Assert.True(ClipLibrary.IsGhostclipName("G", stem));
        Assert.True(ClipLibrary.IsDerived(stem + ".mp4"));
        Assert.Equal(new DateTime(2026, 10, 30, 21, 14, 2), ClipLibrary.TimeFromName(stem + ".mp4"));
    }
}
