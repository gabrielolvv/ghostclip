using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The library's selection: the page sends the whole list, so favorites are written once.
public class SelectClipsTests
{
    [Fact]
    public void The_page_can_send_several_paths()
    {
        var request = JsonSerializer.Deserialize("""{"type":"favoriteMany","paths":["a.mp4","b.mp4"],"favorite":true}""", LibraryJson.Default.LibraryRequest)!;
        Assert.Equal("favoriteMany", request.Type);
        Assert.Equal(["a.mp4", "b.mp4"], request.Paths);
        Assert.True(request.Favorite);
    }

    [Fact]
    public void The_page_is_told_how_many_moved_and_how_many_stayed()
    {
        var json = JsonSerializer.Serialize(new LibraryEvent("done", Op: "deleteMany", Moved: 3, Kept: 1), LibraryJson.Default.LibraryEvent);
        Assert.Contains("\"moved\":3", json);
        Assert.Contains("\"kept\":1", json);
    }

    [Fact]
    public void Requests_for_one_clip_have_no_list()
    {
        Assert.Null(JsonSerializer.Deserialize("""{"type":"favorite","path":"a.mp4"}""", LibraryJson.Default.LibraryRequest)!.Paths);
    }
}
