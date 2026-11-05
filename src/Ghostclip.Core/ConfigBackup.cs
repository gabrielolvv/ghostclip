using System.Text.Json;

namespace Ghostclip.Core;

/// <summary>
/// Settings in a file of their own, to take to another PC or keep through a reinstall. The Discord webhook is a
/// secret and FFmpeg's path belongs to one PC, so neither goes in or comes back. Importing only fills the settings
/// page: saving there still checks the keys and says what it will delete.
/// </summary>
public static class ConfigBackup
{
    private const string Marker = "ghostclipSettingsBackup";
    private const int Version = 1;

    public static string Export(GhostclipConfig config)
    {
        var settings = JsonSerializer.Serialize(config with { DiscordWebhook = null, FfmpegPath = null }, CoreJson.Indented.GhostclipConfig);
        return $"{{\n  \"{Marker}\": {Version},\n  \"config\": {settings}\n}}\n";
    }

    /// <summary>The file's settings over what this PC must keep. Throws <see cref="InvalidOperationException"/> with a message to show.</summary>
    public static GhostclipConfig Import(string file, GhostclipConfig current, Func<string, bool> folderExists)
    {
        GhostclipConfig imported;
        try
        {
            using var document = JsonDocument.Parse(file, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(Marker, out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number)
                || !root.TryGetProperty("config", out var settings) || settings.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(NotABackup());
            }
            if (number > Version)
            {
                throw new InvalidOperationException(UiText.L("This backup was made by a newer Ghostclip. Update first.",
                    "Este backup foi feito por um Ghostclip mais novo. Atualize primeiro.", "Esta copia la hizo un Ghostclip más nuevo. Actualiza primero."));
            }
            imported = settings.Deserialize(CoreJson.Default.GhostclipConfig) ?? throw new InvalidOperationException(NotABackup());
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(NotABackup());
        }

        return imported with
        {
            DiscordWebhook = current.DiscordWebhook,
            FfmpegPath = current.FfmpegPath,
            ClipsFolder = Folder(imported.ClipsFolder, current.ClipsFolder, folderExists),
            BufferFolder = Folder(imported.BufferFolder, current.BufferFolder, folderExists),
        };
    }

    // Another PC's drive may not be here: nothing would save, so what's here stays.
    private static string? Folder(string? imported, string? current, Func<string, bool> exists) =>
        imported is null || exists(imported) ? imported : current;

    private static string NotABackup() => UiText.L("That isn't a Ghostclip settings backup.", "Esse não é um backup de configurações do Ghostclip.", "Eso no es una copia de la configuración de Ghostclip.");
}
