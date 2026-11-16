using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// In a long recording, the moments the sound stands out: where the player marks "loud moments" to jump to.
public class LoudMomentsTests
{
    // 400 buckets of a quiet recording, with spikes where asked.
    private static float[] Quiet(params (int At, float Level)[] spikes)
    {
        var peaks = Enumerable.Repeat(0.2f, ClipMedia.PeakCount).ToArray();
        foreach (var (at, level) in spikes) peaks[at] = level;
        return peaks;
    }

    [Fact]
    public void Spikes_far_apart_are_each_a_moment_a_little_before_the_peak()
    {
        // 400 s in 400 buckets: a bucket a second.
        var moments = ClipMedia.LoudMoments(Quiet((300, 0.95f), (100, 0.9f)), 400);
        Assert.Equal([98.0, 298.0], moments);
    }

    [Fact]
    public void Spikes_close_together_are_one_moment_the_loudest()
    {
        var moments = ClipMedia.LoudMoments(Quiet((100, 0.9f), (110, 0.95f)), 400);
        Assert.Equal([108.0], moments);
    }

    [Fact]
    public void There_are_eight_at_most_the_loudest_of_them()
    {
        var spikes = Enumerable.Range(0, 12).Select(i => (i * 30 + 10, 0.8f + i * 0.01f)).ToArray();
        var moments = ClipMedia.LoudMoments(Quiet(spikes), 400);
        Assert.Equal(8, moments.Count);
        Assert.Equal(128.0, moments[0]);
        Assert.Equal(moments.Order(), moments);
    }

    [Fact]
    public void A_recording_that_is_loud_all_along_has_no_moment()
    {
        var peaks = Enumerable.Repeat(0.8f, ClipMedia.PeakCount).ToArray();
        peaks[200] = 1f;
        Assert.Empty(ClipMedia.LoudMoments(peaks, 400));
    }

    [Fact]
    public void A_short_clip_is_its_own_moment() =>
        Assert.Empty(ClipMedia.LoudMoments(Quiet((100, 0.9f)), 60));

    [Fact]
    public void Silence_or_no_sound_has_none()
    {
        Assert.Empty(ClipMedia.LoudMoments(new float[ClipMedia.PeakCount], 400));
        Assert.Empty(ClipMedia.LoudMoments([], 400));
    }

    [Fact]
    public void A_moment_at_the_very_start_stays_at_zero() =>
        Assert.Equal([0.0], ClipMedia.LoudMoments(Quiet((1, 0.9f)), 400));

    [Fact]
    public void The_details_of_a_clip_carry_them()
    {
        var meta = new ClipMeta(400, 1080, 60, 1, "h264", [0], Quiet((300, 0.95f)));
        Assert.Equal([298.0], meta.LoudMoments);
    }

    [Fact]
    public void The_page_gets_them_with_the_details()
    {
        var meta = new ClipMeta(400, 1080, 60, 1, "h264", [0], Quiet((300, 0.95f)));
        var json = JsonSerializer.Serialize(new LibraryEvent("meta", Meta: meta), LibraryJson.Default.LibraryEvent);
        Assert.Contains("\"loudMoments\":[298]", json);
    }

    [Fact]
    public void Details_cached_with_them_or_before_them_read_back_alike()
    {
        var meta = new ClipMeta(400, 1080, 60, 1, "h264", [0], Quiet((300, 0.95f)));
        var json = JsonSerializer.Serialize(meta, LibraryJson.Default.ClipMeta);
        var back = JsonSerializer.Deserialize(json, LibraryJson.Default.ClipMeta)!;
        Assert.Equal([298.0], back.LoudMoments);
        var older = JsonSerializer.Deserialize(json.Replace(",\"loudMoments\":[298]", ""), LibraryJson.Default.ClipMeta)!;
        Assert.Equal([298.0], older.LoudMoments);
    }
}
