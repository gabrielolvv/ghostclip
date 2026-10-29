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
            problem = $"{Path.GetFileName(path)} is invalid ({ex.Message}). Using defaults; the old file was kept as {Path.GetFileName(aside)}.";
            return new GhostclipConfig();
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
        File.Move(temp, path, overwrite: true);
    }
}
