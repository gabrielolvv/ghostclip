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
