using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Stream Deck, macros and mouse software run Ghostclip.exe with an argument, and the running copy does it.
public class TrayCommandsTests
{
    [Theory]
    [InlineData("--save", TrayCommand.SaveClip)]
    [InlineData("--save-short", TrayCommand.ShortClip)]
    [InlineData("--screenshot", TrayCommand.Screenshot)]
    [InlineData("--session", TrayCommand.Session)]
    [InlineData("--mute", TrayCommand.MicMute)]
    [InlineData("--pause", TrayCommand.Pause)]
    public void Each_argument_names_its_action(string argument, TrayCommand command)
    {
        Assert.Equal(command, TrayCommands.Parse(argument));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--library")]
    [InlineData("--trim")]
    [InlineData("save")]
    public void Anything_else_is_not_a_command(string? argument)
    {
        Assert.Null(TrayCommands.Parse(argument));
    }

    [Fact]
    public void Every_command_has_an_argument_and_an_event_of_its_own()
    {
        var commands = Enum.GetValues<TrayCommand>();
        Assert.Equal(commands.Length, commands.Select(TrayCommands.Argument).Distinct().Count());
        Assert.Equal(commands.Length, commands.Select(TrayCommands.EventName).Distinct().Count());
        Assert.All(commands, c => Assert.Equal(c, TrayCommands.Parse(TrayCommands.Argument(c))));
    }

    [Fact]
    public void The_events_stay_in_the_user_s_session()
    {
        Assert.All(Enum.GetValues<TrayCommand>(), c => Assert.StartsWith(@"Local\Ghostclip.", TrayCommands.EventName(c)));
    }
}
