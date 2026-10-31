using System.Globalization;
using System.Text;

namespace Ghostclip.Core;

public static class ClipNaming
{
    public const string FallbackGame = "Desktop";
    private const int MaxGameNameLength = 60;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Turns a game name into something safe for a folder and file name.</summary>
    public static string SafeGameName(string? game)
    {
        if (string.IsNullOrWhiteSpace(game))
        {
            return FallbackGame;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(game.Length);
        var lastWasSpace = false;
        foreach (var c in game.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            lastWasSpace = false;
            sb.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '_' : c);
        }

        var name = sb.ToString();
        if (name.Length > MaxGameNameLength) name = name[..MaxGameNameLength];
        name = name.TrimEnd('.', ' ');
        if (name.Length == 0) return FallbackGame;

        // "CON" or "nul.txt" can't be a file name on Windows.
        var stem = name.Split('.')[0];
        return ReservedNames.Contains(stem) ? name + "_" : name;
    }

    /// <summary>A sibling of an existing clip: Game_..._trim.mp4, Game_..._discord.mp4, deduplicated.</summary>
    public static string DerivedPath(string clipPath, string suffix, Func<string, bool> exists)
    {
        var dir = Path.GetDirectoryName(clipPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(clipPath) + "_" + suffix;
        var path = Path.Combine(dir, stem + ".mp4");
        for (var n = 2; exists(path); n++)
        {
            path = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{stem}_{n}.mp4"));
        }
        return path;
    }

    /// <summary>
    /// &lt;root&gt;\&lt;Game&gt;\&lt;Game&gt;_YYYY-MM-DD_HH-MM-SS.mp4, with _2, _3... when the
    /// name is taken (two saves in the same second). Screenshots pass ".jpg".
    /// </summary>
    public static string BuildPath(string root, string? game, DateTime localTime, Func<string, bool> exists, string extension = ".mp4")
    {
        var safe = SafeGameName(game);
        var dir = Path.Combine(root, safe);
        var stem = string.Create(CultureInfo.InvariantCulture, $"{safe}_{localTime:yyyy-MM-dd_HH-mm-ss}");
        var path = Path.Combine(dir, stem + extension);
        for (var n = 2; exists(path); n++)
        {
            path = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{stem}_{n}{extension}"));
        }
        return path;
    }

    /// <summary>
    /// The path <paramref name="path"/> gets when renamed to <paramref name="name"/>: same folder,
    /// same extension. Null when the name doesn't change. Throws, with a message for
    /// people, on a name Windows can't use or one another file already has.
    /// </summary>
    public static string? RenamedPath(string path, string name, Func<string, bool> exists)
    {
        var extension = Path.GetExtension(path);
        var stem = name.Trim();
        // "Clip.mp4" typed in full means the same as "Clip".
        if (stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) stem = stem[..^extension.Length].TrimEnd();
        if (stem.Length == 0)
        {
            throw new InvalidOperationException(UiText.L("Type a name.", "Digite um nome."));
        }
        if (stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stem.EndsWith('.') || ReservedNames.Contains(stem.Split('.')[0]))
        {
            throw new InvalidOperationException(UiText.L(
                "Windows can't use that name. Leave out \\ / : * ? \" < > | and don't end it with a dot.",
                "O Windows não aceita esse nome. Tire \\ / : * ? \" < > | e não termine com ponto."));
        }
        if (stem.Length > 150)
        {
            throw new InvalidOperationException(UiText.L("That name is too long.", "Esse nome é longo demais."));
        }
        var target = Path.Combine(Path.GetDirectoryName(path) ?? "", stem + extension);
        if (string.Equals(target, path, StringComparison.Ordinal)) return null;
        // Only the case changed: the same file on Windows, so it's not taken.
        if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase) && exists(target))
        {
            throw new InvalidOperationException(UiText.L($"There is already a file called {stem}{extension} here.", $"Já existe um arquivo chamado {stem}{extension} aqui."));
        }
        return target;
    }
}
