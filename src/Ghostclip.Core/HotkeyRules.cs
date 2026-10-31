using System.Text.RegularExpressions;

namespace Ghostclip.Core;

public static partial class HotkeyRules
{
    /// <summary>
    /// Whether <paramref name="key"/> may be the hotkey with no Ctrl, Alt, Shift or Win.
    /// A registered hotkey takes the key away from every other program, so only keys
    /// people don't type with qualify: F1-F24, Pause, Scroll Lock and the numpad digits.
    /// </summary>
    public static bool AllowedWithoutModifier(string key) => Alone().IsMatch(key.Trim());

    // Shortcuts people use all day in every program. Mirrored in settings.js (COMMON).
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+S", "Ctrl+F", "Ctrl+P",
        "Ctrl+N", "Ctrl+T", "Ctrl+W", "Ctrl+R", "Ctrl+Shift+Z", "Ctrl+Shift+T", "Alt+F4", "Alt+Tab",
    };

    /// <summary>
    /// Whether <paramref name="gesture"/> is a shortcut programs use all the time. A
    /// registered hotkey takes it away from every one of them: as the clip hotkey,
    /// Ctrl+C stopped copying anywhere in Windows.
    /// </summary>
    public static bool TakesCommonShortcut(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var token in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win" or "windows": win = true; break;
                default: key = token; break;
            }
        }
        if (key is null) return false;
        var canonical = string.Join('+', new[] { ctrl ? "Ctrl" : null, alt ? "Alt" : null, shift ? "Shift" : null, win ? "Win" : null, key }
            .Where(t => t is not null));
        return Common.Contains(canonical);
    }

    /// <summary>
    /// The hotkey as people read it: "Ctrl+Oemtilde" becomes "Ctrl+´" when <paramref name="keyLabel"/>
    /// knows what that key prints on this keyboard. Tokens it doesn't know stay as they are.
    /// </summary>
    public static string Label(string gesture, Func<string, string?> keyLabel) =>
        string.Join('+', gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => keyLabel(token) ?? token));

    [GeneratedRegex(@"^(F([1-9]|1[0-9]|2[0-4])|Pause|Scroll|NumPad[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex Alone();
}
