using System.Text.RegularExpressions;

namespace Ghostclip.Core;

/// <summary>
/// Names the clip folder after the foreground program. Uses only the process
/// name and the version resource of its .exe on disk: nothing is read from
/// the game's memory.
/// </summary>
public static partial class GameNameResolver
{
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
        "ApplicationFrameHost", "LockApp", "TextInputHost", "ShellHost", "dwm",
        "Ghostclip", "Ghostclip.Worker",
    };

    // Engine or OS names that say nothing about which game it is.
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Windows Operating System", "Unreal Engine", "UnrealEngine", "UE4", "UE5",
        "Unity", "Unity Player", "BootstrapPackagedGame", "Game", "Launcher",
    };

    public static string Resolve(string? processName, string? productName, string? fileDescription)
    {
        if (string.IsNullOrWhiteSpace(processName) || ShellProcesses.Contains(processName))
        {
            return ClipNaming.FallbackGame;
        }

        foreach (var candidate in new[] { productName, fileDescription })
        {
            var clean = Clean(candidate);
            if (clean.Length > 0 && !GenericNames.Contains(clean)) return clean;
        }

        var stripped = BuildSuffix().Replace(processName, "");
        return stripped.Length > 0 ? stripped : processName;
    }

    private static string Clean(string? value) =>
        value is null ? "" : Whitespace().Replace(value.Replace("®", "").Replace("™", "").Replace("©", ""), " ").Trim();

    [GeneratedRegex(@"(-WinGDK-Shipping|-Win64-Shipping|-Shipping|-Win64|_x64|_DX1[12])$", RegexOptions.IgnoreCase)]
    private static partial Regex BuildSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
