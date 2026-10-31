using Ghostclip.Core;

namespace Ghostclip.Host;

/// <summary>
/// Receives the save hotkey through RegisterHotKey. Windows only tells us when
/// this exact combination is pressed: there is no keyboard hook and nothing
/// runs inside the game.
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x4743;
    private static readonly nint HWND_MESSAGE = -3;
    private bool _registered;

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = HWND_MESSAGE });

    public event Action? Pressed;

    public bool TryRegister(string gesture, out string? error)
    {
        Unregister();
        if (!TryParse(gesture, out var modifiers, out var key, out error))
        {
            return false;
        }
        if (!Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, (uint)key))
        {
            error = L($"{gesture} is already taken by another program.", $"{gesture} já está em uso por outro programa.");
            return false;
        }
        _registered = true;
        return true;
    }

    /// <summary>Parses "Alt+F10", "Ctrl+Shift+F9", "Win+Alt+9"...</summary>
    public static bool TryParse(string gesture, out uint modifiers, out Keys key, out string? error)
    {
        modifiers = 0;
        key = Keys.None;
        error = null;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "alt": modifiers |= Native.MOD_ALT; continue;
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; continue;
                case "shift": modifiers |= Native.MOD_SHIFT; continue;
                case "win" or "windows": modifiers |= Native.MOD_WIN; continue;
            }
            // "9" means the 9 key (Keys.D9), not the enum value 9.
            var token = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
            if (key != Keys.None || int.TryParse(token, out _) || !Enum.TryParse(token, ignoreCase: true, out key) ||
                key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                error = L($"\"{gesture}\" is not a valid hotkey. Use something like Alt+F10.",
                    $"\"{gesture}\" não é uma hotkey válida. Use algo como Alt+F10.");
                key = Keys.None;
                return false;
            }
        }
        if (key == Keys.None)
        {
            error = L($"\"{gesture}\" has no main key. Use something like Alt+F10.",
                $"\"{gesture}\" não tem uma tecla principal. Use algo como Alt+F10.");
            return false;
        }
        if (modifiers == 0 && !HotkeyRules.AllowedWithoutModifier(key.ToString()))
        {
            error = L($"\"{gesture}\" alone would stop working in every other program. Add Ctrl, Alt, Shift or Win, or use an F key.",
                $"\"{gesture}\" sozinha deixaria de funcionar em todos os outros programas. Adicione Ctrl, Alt, Shift ou Win, ou use uma tecla F.");
            key = Keys.None;
            return false;
        }
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == HotkeyId)
        {
            Pressed?.Invoke();
        }
        base.WndProc(ref m);
    }

    private void Unregister()
    {
        if (_registered)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
