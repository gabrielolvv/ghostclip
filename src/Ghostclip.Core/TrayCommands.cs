namespace Ghostclip.Core;

/// <summary>What another launch of Ghostclip.exe can ask the running copy to do.</summary>
public enum TrayCommand { SaveClip, ShortClip, Screenshot, Session, MicMute, Pause }

/// <summary>
/// For Stream Deck, macros and mouse software: "Ghostclip.exe --save" and the like do what the hotkeys do, without
/// taking a key from other programs. The second launch sets a named event in this session and exits.
/// </summary>
public static class TrayCommands
{
    public static string Argument(TrayCommand command) => command switch
    {
        TrayCommand.SaveClip => "--save",
        TrayCommand.ShortClip => "--save-short",
        TrayCommand.Screenshot => "--screenshot",
        TrayCommand.Session => "--session",
        TrayCommand.MicMute => "--mute",
        TrayCommand.Pause => "--pause",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    public static TrayCommand? Parse(string? argument)
    {
        foreach (var command in Enum.GetValues<TrayCommand>())
        {
            if (Argument(command) == argument) return command;
        }
        return null;
    }

    public static string EventName(TrayCommand command) => $@"Local\Ghostclip.Command.{command}";
}
