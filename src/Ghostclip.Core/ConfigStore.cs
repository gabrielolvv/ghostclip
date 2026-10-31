using System.Text.Json;

namespace Ghostclip.Core;

public static class ConfigStore
{
    /// <summary>
    /// Loads the config, or the defaults when the file is missing. A broken file
    /// never stops the app: it is renamed aside and the defaults are used, with
    /// the reason in <paramref name="problem"/>.
    /// </summary>
    public static GhostclipConfig Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
        {
            var defaults = new GhostclipConfig();
            Save(path, defaults);
            return defaults;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var config = JsonSerializer.Deserialize(stream, CoreJson.Default.GhostclipConfig)
                ?? throw new JsonException("The file is empty.");
            return config.Normalize();
        }
        catch (JsonException ex)
        {
            var aside = path + ".invalid";
            File.Copy(path, aside, overwrite: true);
            // Shown in the language Windows uses: the file that says otherwise is the one that can't be read.
            problem = UiText.L(
                $"{Path.GetFileName(path)} is invalid ({ex.Message}). Using defaults; the old file was kept as {Path.GetFileName(aside)}.",
                $"{Path.GetFileName(path)} é inválido ({ex.Message}). Usando os padrões; o arquivo antigo foi guardado como {Path.GetFileName(aside)}.");
            return new GhostclipConfig();
        }
    }

    /// <summary>
    /// Reads the file as it is now, for applying changes live. Unlike <see cref="Load"/>
    /// it never writes: a file caught half-way through a hand edit is reported and
    /// left alone, and the settings in use stay as they are.
    /// </summary>
    public static bool TryRead(string path, out GhostclipConfig? config, out string? error)
    {
        config = null;
        try
        {
            using var stream = File.OpenRead(path);
            config = (JsonSerializer.Deserialize(stream, CoreJson.Default.GhostclipConfig)
                ?? throw new JsonException("The file is empty.")).Normalize();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Writes atomically: a crash mid-write leaves the previous file intact.</summary>
    public static void Save(string path, GhostclipConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, config, CoreJson.Indented.GhostclipConfig);
        }
        // A reader (the tray applying the last save, an editor, antivirus) can hold the file for a moment.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
            {
                Thread.Sleep(50);
            }
            catch
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
