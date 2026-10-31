using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghostclip.Core;

/// <summary>
/// What the library page asks for: list, meta, trim, compress, copy, reveal,
/// delete, favorite (Favorite) or rename (Name) for clips (Path is a clip's full
/// path as the page received it), and openFolder; settings, saveSettings
/// (Config, Confirmed once the page has warned that saving restarts recording),
/// pickFolder and pickFfmpeg for the settings page.
/// </summary>
public sealed record LibraryRequest(string Type, string? Path = null, double Start = 0, double End = 0,
    GhostclipConfig? Config = null, bool Confirmed = false, string? Name = null, bool Favorite = false);

/// <summary>
/// A clip or screenshot as the page sees it. Url plays or shows it through the
/// library's virtual host. Version changes when the file does: a trim deleted and
/// made again reuses the name, and the page must not show the old one's details
/// for it. Bookmarks are seconds a session recording marked.
/// </summary>
public sealed record ClipDto(string Path, string Url, string Game, string Saved, long Bytes, bool Derived, string Version,
    bool Image = false, bool Favorite = false, IReadOnlyList<double>? Bookmarks = null)
{
    public const string ClipsHost = "https://clips.ghostclip/";

    public static ClipDto From(ClipEntry e, LibraryState? state = null)
    {
        var bookmarks = state?.BookmarksFor(e.Path);
        return new(e.Path, ClipsHost + e.RelativeUrl, e.Game,
            e.SavedLocal.ToString("s", CultureInfo.InvariantCulture), e.Bytes, e.Derived,
            string.Create(CultureInfo.InvariantCulture, $"{e.Bytes}-{e.LastWriteUtc.Ticks}"),
            e.Image, state?.IsFavorite(e.Path) ?? false, bookmarks is { Count: > 0 } ? bookmarks : null);
    }
}

/// <summary>The library page's own origin: the only one the bridge answers or the window navigates to.</summary>
public static class LibraryOrigin
{
    public const string AppHost = "app.ghostclip";

    public static bool IsApp(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        string.Equals(u.Host, AppHost, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What the app tells the page: clips (the list), meta (one clip's details and
/// thumbnail), open (show this clip), done and error (an action's result),
/// changed (the clips folder changed), settings (Config, Encoders this PC
/// supports, Games for profile names, HotkeyLabels: each hotkey setting's
/// value as this keyboard prints it), confirm (saving restarts recording:
/// Message says what it clears), picked (a chosen Path for Op), status (the
/// recorder badge, Status null when the tray isn't running), showSettings and
/// language (Language is "en" or "pt"; the page opens with ?lang= and this
/// switches it live).
/// </summary>
public sealed record LibraryEvent(string Type, IReadOnlyList<ClipDto>? Clips = null, string? Root = null, string? Path = null,
    ClipMeta? Meta = null, string? Thumb = null, string? Output = null, string? Op = null, string? Message = null,
    GhostclipConfig? Config = null, IReadOnlyList<string>? Encoders = null, IReadOnlyList<string>? Games = null,
    RecorderStatus? Status = null, IReadOnlyDictionary<string, string>? HotkeyLabels = null, string? Language = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(LibraryRequest))]
[JsonSerializable(typeof(LibraryEvent))]
[JsonSerializable(typeof(ClipMeta))]
[JsonSerializable(typeof(RecorderStatus))]
[JsonSerializable(typeof(WindowBounds))]
public sealed partial class LibraryJson : JsonSerializerContext
{
    /// <summary>Same contract, indented: for files people may open.</summary>
    public static LibraryJson Indented { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    });
}
