using System.Runtime.InteropServices;
using System.Text;
using Ghostclip.Core;

namespace Ghostclip.Host;

/// <summary>
/// Hotkeys as people read them. The stored name follows Windows' key list
/// ("Ctrl+Oemtilde"); the label asks the keyboard layout what that key prints
/// ("Ctrl+´" on ABNT2, "Ctrl+`" on US) and what the numpad keys are called.
/// </summary>
internal static partial class KeyLabels
{
    private const uint MAPVK_VK_TO_VSC = 0;

    public static string Describe(string gesture) => HotkeyRules.Label(gesture, LabelFor);

    // Windows names some of these oddly ("Num Del" for the decimal key, ";" for divide without the extended bit).
    private static readonly Dictionary<string, string> NumpadOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Add"] = "Num +", ["Subtract"] = "Num -", ["Multiply"] = "Num *", ["Divide"] = "Num /", ["Decimal"] = "Num .", ["Separator"] = "Num ,",
    };

    private static string? LabelFor(string token)
    {
        if (NumpadOperators.TryGetValue(token, out var op)) return op;
        // Only keys whose stored name means nothing to people; Ctrl, F10 and A read fine as they are.
        if (!(token.StartsWith("Oem", StringComparison.OrdinalIgnoreCase) || token.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if (!Enum.TryParse<Keys>(token, ignoreCase: true, out var key)) return null;
        var scan = MapVirtualKeyW((uint)key, MAPVK_VK_TO_VSC);
        if (scan == 0) return null;
        var text = new StringBuilder(32);
        var length = GetKeyNameTextW((int)(scan << 16), text, text.Capacity);
        if (length <= 0) return null;
        return text.ToString(0, length);
    }

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder text, int size);
}
