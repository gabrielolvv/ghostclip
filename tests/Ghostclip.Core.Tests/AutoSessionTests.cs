using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// When a game's profile asks for it, its session records from when it opens until it closes.
public class AutoSessionTests
{
    private const uint Game = 100, OtherRun = 200, Launcher = 300;

    private readonly HashSet<uint> _running = [Game];
    private readonly AutoSession _auto = new();

    private AutoSessionStep Tick(uint front = Game, bool asked = true, bool session = false, bool canStart = true, string name = "Doom") =>
        _auto.Next(front, name, asked, session, canStart, _running.Contains);

    [Fact]
    public void A_game_that_asks_starts_its_session()
    {
        Assert.Equal(AutoSessionStep.Start, Tick());
    }

    [Theory]
    [InlineData(0u, true, false, true)]      // Nothing in front that could be a game.
    [InlineData(Game, false, false, true)]   // Its profile doesn't ask.
    [InlineData(Game, true, true, true)]     // A session is already going, started by hand.
    [InlineData(Game, true, false, false)]   // Not recording right now.
    public void Otherwise_nothing_starts(uint front, bool asked, bool session, bool canStart)
    {
        Assert.Equal(AutoSessionStep.None, Tick(front, asked, session, canStart));
    }

    [Fact]
    public void Starting_waits_for_recording_to_be_up()
    {
        Assert.Equal(AutoSessionStep.None, Tick(canStart: false));
        Assert.Equal(AutoSessionStep.Start, Tick());
    }

    [Fact]
    public void It_goes_on_while_the_game_runs_even_with_something_else_in_front()
    {
        Tick();
        Assert.Equal(AutoSessionStep.None, Tick(session: true));
        Assert.Equal(AutoSessionStep.None, Tick(front: 0, asked: false, session: true));
    }

    [Fact]
    public void It_stops_when_the_game_closes_and_only_once()
    {
        Tick();
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.Stop, Tick(front: 0, asked: false, session: true));
        Assert.Equal(AutoSessionStep.None, Tick(front: 0, asked: false, session: false));
    }

    [Fact]
    public void A_session_started_by_hand_is_never_stopped()
    {
        Assert.Equal(AutoSessionStep.None, Tick(session: true));
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.None, Tick(front: 0, asked: false, session: true));
    }

    [Fact]
    public void Stopped_by_hand_it_stays_stopped_for_this_run_of_the_game()
    {
        Tick();
        Assert.Equal(AutoSessionStep.None, Tick(session: false));
        Assert.Equal(AutoSessionStep.None, Tick(session: false));
    }

    [Fact]
    public void The_next_run_of_the_game_starts_again()
    {
        Tick();
        Tick(session: false);
        _running.Remove(Game);
        _running.Add(OtherRun);
        Assert.Equal(AutoSessionStep.Start, Tick(front: OtherRun));
    }

    [Fact]
    public void A_session_that_ended_with_the_game_lets_the_next_run_start()
    {
        Tick();
        _running.Remove(Game);
        Tick(front: 0, asked: false, session: true);
        _running.Add(OtherRun);
        Assert.Equal(AutoSessionStep.Start, Tick(front: OtherRun));
    }

    [Fact]
    public void A_session_going_for_one_game_isnt_taken_over_by_another()
    {
        Tick();
        _running.Add(OtherRun);
        Assert.Equal(AutoSessionStep.None, Tick(front: OtherRun, session: true, name: "Quake"));
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.Stop, Tick(front: OtherRun, session: true, name: "Quake"));
    }

    [Fact]
    public void A_session_started_by_hand_and_stopped_by_hand_stays_stopped()
    {
        Assert.Equal(AutoSessionStep.None, Tick(session: true));
        Assert.Equal(AutoSessionStep.None, Tick(session: false));
        Assert.Equal(AutoSessionStep.None, Tick(session: false));
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.None, Tick(front: 0, asked: false));
        _running.Add(OtherRun);
        Assert.Equal(AutoSessionStep.Start, Tick(front: OtherRun));
    }

    [Fact]
    public void A_launcher_that_closes_while_the_game_runs_doesnt_end_the_session()
    {
        _running.Add(Launcher);
        Assert.Equal(AutoSessionStep.Start, Tick(front: Launcher));
        Assert.Equal(AutoSessionStep.None, Tick(front: Game, session: true));
        _running.Remove(Launcher);
        Assert.Equal(AutoSessionStep.None, Tick(front: Game, session: true));
        Assert.Equal(AutoSessionStep.None, Tick(front: 0, asked: false, session: true));
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.Stop, Tick(front: 0, asked: false, session: true));
    }

    [Fact]
    public void Stopped_by_hand_covers_the_whole_game_not_only_the_launcher()
    {
        _running.Add(Launcher);
        Tick(front: Launcher);
        Assert.Equal(AutoSessionStep.None, Tick(front: Game, session: false));
        _running.Remove(Launcher);
        Assert.Equal(AutoSessionStep.None, Tick(front: Game, session: false));
    }

    [Fact]
    public void A_launcher_handing_over_between_two_looks_keeps_the_one_session()
    {
        _running.Add(Launcher);
        Tick(front: Launcher);
        _running.Remove(Launcher);
        Assert.Equal(AutoSessionStep.None, Tick(front: Game, session: true));
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.Stop, Tick(front: 0, asked: false, session: true));
    }

    [Fact]
    public void It_knows_when_the_session_going_is_its_own()
    {
        Assert.False(_auto.Started);
        Tick();
        Assert.True(_auto.Started);
        _running.Remove(Game);
        Tick(front: 0, asked: false, session: true);
        Assert.False(_auto.Started);
    }

    [Fact]
    public void A_pid_reused_after_declining_doesnt_matter_once_it_was_seen_gone()
    {
        Tick();
        Tick(session: false);
        _running.Remove(Game);
        Assert.Equal(AutoSessionStep.None, Tick(front: 0, asked: false));
        _running.Add(Game);
        Assert.Equal(AutoSessionStep.Start, Tick());
    }

    [Fact]
    public void The_profile_says_so_and_it_is_off_by_default()
    {
        var config = new GhostclipConfig { Profiles = [new GameProfile { Game = "Doom" }, new GameProfile { Game = "Quake", AutoSession = true }] }.Normalize();
        Assert.False(config.AutoSessionFor("Doom"));
        Assert.True(config.AutoSessionFor("quake"));
        Assert.False(config.AutoSessionFor("Other"));
        Assert.False(config.AutoSessionFor(null));
        Assert.True(new GhostclipConfig().SameCaptureAs(new GhostclipConfig { Profiles = [new GameProfile { Game = "Quake", AutoSession = true }] }));
    }
}
