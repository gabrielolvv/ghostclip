using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghostclip.Core;

/// <summary>
/// What the library page asks for: list, meta, trim, compress, copy, reveal,
/// delete or openFolder. Path is a clip's full path as the page received it.
/// </summary>
public sealed record LibraryRequest(string Type, string? Path = null, double Start = 0, double End = 0);

/// <summary>A clip as the page sees it. Url plays it through the library's virtual host.</summary>
public sealed record ClipDto(string Path, string Url, string Game, string Saved, long Bytes, bool Derived)
{
    public const string ClipsHost = "https://clips.ghostclip/";

    public static ClipDto From(ClipEntry e) => new(e.Path, ClipsHost + e.RelativeUrl, e.Game,
        e.SavedLocal.ToString("s", CultureInfo.InvariantCulture), e.Bytes, e.Derived);
}

/// <summary>
/// What the app tells the page: clips (the list), meta (one clip's details and
/// thumbnail), open (show this clip), done and error (an action's result) and
/// changed (the clips folder changed).
/// </summary>
public sealed record LibraryEvent(string Type, IReadOnlyList<ClipDto>? Clips = null, string? Root = null, string? Path = null,
    ClipMeta? Meta = null, string? Thumb = null, string? Output = null, string? Op = null, string? Message = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(LibraryRequest))]
[JsonSerializable(typeof(LibraryEvent))]
[JsonSerializable(typeof(ClipMeta))]
public sealed partial class LibraryJson : JsonSerializerContext;
