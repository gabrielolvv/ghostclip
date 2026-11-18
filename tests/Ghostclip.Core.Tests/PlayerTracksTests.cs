using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The player hears every track: the microphone and Discord come apart from the clip, by stream copy, to play beside it.
public class PlayerTracksTests
{
    private static string Arg(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void A_track_comes_out_as_it_is_with_no_picture()
    {
        var args = FfmpegArgs.ExtractTrack("in.mp4", "out.mp4", 1);
        Assert.Equal("in.mp4", Arg(args, "-i"));
        Assert.Equal("0:a:1", Arg(args, "-map"));
        Assert.Equal("copy", Arg(args, "-c"));
        Assert.Contains("-vn", args);
        Assert.Equal("mp4", Arg(args, "-f"));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void Its_file_sits_in_the_cache_under_the_clip_key_and_is_pruned_with_it()
    {
        var name = ClipMedia.TrackName("0123456789abcdef", 2);
        Assert.Equal("0123456789abcdef.track2.mp4", name);

        var dir = Directory.CreateTempSubdirectory("ghostclip-tracks-").FullName;
        try
        {
            var old = Path.Combine(dir, name);
            File.WriteAllText(old, "");
            File.SetLastWriteTimeUtc(old, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var fresh = Path.Combine(dir, ClipMedia.TrackName("fedcba9876543210", 1));
            File.WriteAllText(fresh, "");
            File.SetLastWriteTimeUtc(fresh, new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(1, ClipMedia.PruneCache(dir, TimeSpan.FromDays(90), new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_track_unused_for_a_week_is_pruned_as_it_comes_back_from_the_clip_in_seconds()
    {
        var dir = Directory.CreateTempSubdirectory("ghostclip-tracks-").FullName;
        try
        {
            var now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            var track = Path.Combine(dir, ClipMedia.TrackName("0123456789abcdef", 1));
            var thumb = Path.Combine(dir, "0123456789abcdef.jpg");
            foreach (var f in new[] { track, thumb })
            {
                File.WriteAllText(f, "");
                File.SetLastWriteTimeUtc(f, now.AddDays(-8));
            }
            var recent = Path.Combine(dir, ClipMedia.TrackName("fedcba9876543210", 1));
            File.WriteAllText(recent, "");
            File.SetLastWriteTimeUtc(recent, now.AddDays(-6));

            Assert.Equal(1, ClipMedia.PruneCache(dir, TimeSpan.FromDays(90), now));
            Assert.False(File.Exists(track));
            Assert.True(File.Exists(thumb));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_page_gets_their_addresses_in_track_order()
    {
        var e = new LibraryEvent("tracks", Path: @"C:\V\G\a.mp4", TrackUrls: ["https://cache.ghostclip/k.track1.mp4", "https://cache.ghostclip/k.track2.mp4"]);
        var json = JsonSerializer.Serialize(e, LibraryJson.Default.LibraryEvent);
        Assert.Contains("\"trackUrls\":[\"https://cache.ghostclip/k.track1.mp4\",\"https://cache.ghostclip/k.track2.mp4\"]", json);
    }
}
