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

    /// <summary>
    /// &lt;root&gt;\&lt;Game&gt;\&lt;Game&gt;_YYYY-MM-DD_HH-MM-SS.mp4, with _2, _3... when the
    /// name is taken (two saves in the same second).
    /// </summary>
    public static string BuildPath(string root, string? game, DateTime localTime, Func<string, bool> exists)
    {
        var safe = SafeGameName(game);
        var dir = Path.Combine(root, safe);
        var stem = string.Create(CultureInfo.InvariantCulture, $"{safe}_{localTime:yyyy-MM-dd_HH-mm-ss}");
        var path = Path.Combine(dir, stem + ".mp4");
        for (var n = 2; exists(path); n++)
        {
            path = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{stem}_{n}.mp4"));
        }
        return path;
    }
}
