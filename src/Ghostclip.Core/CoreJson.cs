using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghostclip.Core;

/// <summary>Source-generated JSON: no reflection, safe for trimming and Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(GhostclipConfig))]
[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(ProbeCache))]
[JsonSerializable(typeof(RecordingSession))]
public sealed partial class CoreJson : JsonSerializerContext
{
    /// <summary>Same contract, indented: for files people may open and edit.</summary>
    public static CoreJson Indented { get; } = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    });
}
