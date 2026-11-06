using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Words people write on a clip to find it again. They live in the library's file, never in the video.
public class ClipTagsTests
{
    private const string Clip = @"C:\Clips\G\G_2026-10-30_21-14-02.mp4";

    [Fact]
    public void Tags_are_kept_and_given_back()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["clutch", "to post"]);
        Assert.Equal(["clutch", "to post"], state.TagsFor(Clip));
    }

    [Fact]
    public void A_clip_with_none_has_none()
    {
        Assert.Empty(new LibraryState().TagsFor(Clip));
    }

    [Fact]
    public void Setting_replaces_and_clearing_leaves_nothing_behind()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["a", "b"]);
        state.SetTags(Clip, ["c"]);
        Assert.Equal(["c"], state.TagsFor(Clip));
        state.SetTags(Clip, []);
        Assert.Empty(state.TagsFor(Clip));
        Assert.Empty(state.Tags);
    }

    [Theory]
    [InlineData(new[] { "  clutch  " }, new[] { "clutch" })]
    [InlineData(new[] { "", "   " }, new string[0])]
    [InlineData(new[] { "to   post" }, new[] { "to post" })]
    [InlineData(new[] { "Clutch", "clutch", "CLUTCH" }, new[] { "Clutch" })]
    public void Tags_are_tidied(string[] typed, string[] kept)
    {
        var state = new LibraryState();
        state.SetTags(Clip, typed);
        Assert.Equal(kept, state.TagsFor(Clip));
    }

    [Fact]
    public void A_long_tag_is_cut_and_a_long_list_stops()
    {
        var state = new LibraryState();
        state.SetTags(Clip, [new string('x', 100)]);
        Assert.Equal(40, state.TagsFor(Clip)[0].Length);
        state.SetTags(Clip, Enumerable.Range(0, 50).Select(i => $"t{i}"));
        Assert.Equal(20, state.TagsFor(Clip).Count);
    }

    [Fact]
    public void Renaming_carries_them_along()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["clutch"]);
        state.Move(Clip, @"C:\Clips\G\Best play.mp4");
        Assert.Equal(["clutch"], state.TagsFor(@"C:\Clips\G\Best play.mp4"));
        Assert.Empty(state.TagsFor(Clip));
    }

    [Fact]
    public void Deleting_takes_them_away()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["clutch"]);
        state.Forget(Clip);
        Assert.Empty(state.TagsFor(Clip));
    }

    [Fact]
    public void A_clips_folder_moved_keeps_them()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["clutch"]);
        Assert.Equal(["clutch"], state.TagsFor(@"E:\Moved\G\G_2026-10-30_21-14-02.mp4"));
    }

    [Fact]
    public void Deleting_a_same_named_copy_elsewhere_leaves_the_originals()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["clutch"]);
        state.Forget(@"E:\Moved\G\G_2026-10-30_21-14-02.mp4");
        Assert.Equal(["clutch"], state.TagsFor(Clip));
    }

    [Fact]
    public void They_survive_the_library_file()
    {
        var state = new LibraryState();
        state.SetTags(Clip, ["clutch"]);
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(state, CoreJson.Default.LibraryState), CoreJson.Default.LibraryState);
        Assert.Equal(["clutch"], back!.TagsFor(Clip));
    }

    [Fact]
    public void A_library_file_from_before_tags_reads_fine()
    {
        var back = JsonSerializer.Deserialize("""{"favorites":[],"bookmarks":{}}""", CoreJson.Default.LibraryState);
        Assert.Empty(back!.TagsFor(Clip));
    }

    [Fact]
    public void The_page_gets_them_only_when_there_are_some()
    {
        var entry = new ClipEntry(Clip, "G/x.mp4", "G", DateTime.Now, 1, false, DateTime.UtcNow);
        var state = new LibraryState();
        Assert.Null(ClipDto.From(entry, state).Tags);
        state.SetTags(Clip, ["clutch"]);
        Assert.Equal(["clutch"], ClipDto.From(entry, state).Tags);
    }

    [Fact]
    public void Request_carries_them_from_the_page()
    {
        var request = JsonSerializer.Deserialize("""{"type":"tags","path":"a.mp4","tags":["a","b"]}""", LibraryJson.Default.LibraryRequest);
        Assert.Equal(["a", "b"], request!.Tags);
    }
}
