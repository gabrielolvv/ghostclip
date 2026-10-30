using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class LibraryMessagesTests
{
    [Fact]
    public void Request_parses_from_page_json()
    {
        var request = JsonSerializer.Deserialize("""{"type":"trim","path":"C:\\c\\a.mp4","start":12.5,"end":40}""", LibraryJson.Default.LibraryRequest);
        Assert.Equal(new LibraryRequest("trim", @"C:\c\a.mp4", 12.5, 40), request);
    }

    [Fact]
    public void Event_omits_nulls()
    {
        var json = JsonSerializer.Serialize(new LibraryEvent("done", Op: "trim", Path: "a.mp4", Output: "a_trim.mp4"), LibraryJson.Default.LibraryEvent);
        Assert.Equal("""{"type":"done","path":"a.mp4","output":"a_trim.mp4","op":"trim"}""", json);
    }

    [Fact]
    public void Meta_round_trips_with_keyframes_and_peaks()
    {
        var meta = new ClipMeta(60.5, 1080, 60, 2, "h264", [0, 2, 4], [0.25f, 1f]);
        var json = JsonSerializer.Serialize(new LibraryEvent("meta", Path: "a.mp4", Meta: meta), LibraryJson.Default.LibraryEvent);
        var back = JsonSerializer.Deserialize(json, LibraryJson.Default.LibraryEvent)!.Meta!;
        Assert.Equal(meta.Duration, back.Duration);
        Assert.Equal(meta.VideoCodec, back.VideoCodec);
        Assert.Equal(meta.Keyframes, back.Keyframes);
        Assert.Equal(meta.Peaks, back.Peaks);
        Assert.Contains("\"videoCodec\":\"h264\"", json);
    }

    [Fact]
    public void Clip_list_carries_urls_for_the_page()
    {
        var entry = new ClipEntry(@"C:\c\G\a b.mp4", "G/a%20b.mp4", "G", new DateTime(2026, 10, 30, 21, 14, 2), 10, false);
        var dto = ClipDto.From(entry);
        Assert.Equal("https://clips.ghostclip/G/a%20b.mp4", dto.Url);
        Assert.Equal("2026-10-30T21:14:02", dto.Saved);
    }
}
