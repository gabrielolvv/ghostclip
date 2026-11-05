namespace Ghostclip.Core;

public enum AutoSessionStep { None, Start, Stop }

/// <summary>
/// Decides when a game whose profile asks for it records its session: from when it's first seen in front until
/// its last process seen in front ends, since a launcher can carry the same name and close first. A session
/// started by hand is never stopped from here. One stopped by hand, or by anything else, stays stopped until
/// the game runs again, and so does a game that was in front of a session started by hand: nobody should have to
/// stop it twice.
/// </summary>
public sealed class AutoSession
{
    private readonly HashSet<uint> _pids = [];
    private string? _game;
    private bool _declined;

    /// <summary>The session going, if there is one, is one this started.</summary>
    public bool Started => _game is not null && !_declined;

    /// <param name="frontPid">The game in front, 0 for anything else.</param>
    /// <param name="frontGame">Its name.</param>
    /// <param name="asked">Its profile asks for a session.</param>
    /// <param name="sessionRunning">A session is going, whoever started it.</param>
    /// <param name="canStart">Recording is up, which a session needs.</param>
    /// <param name="isRunning">Whether a process is still there.</param>
    public AutoSessionStep Next(uint frontPid, string? frontGame, bool asked, bool sessionRunning, bool canStart, Func<uint, bool> isRunning)
    {
        if (_game is not null)
        {
            var inFront = frontPid != 0 && string.Equals(frontGame, _game, StringComparison.OrdinalIgnoreCase);
            if (Started && sessionRunning)
            {
                // A launcher can hand over to the game between two looks: the same name in front carries it on.
                if (inFront) _pids.Add(frontPid);
                if (_pids.Any(isRunning)) return AutoSessionStep.None;
                Forget();
                return AutoSessionStep.Stop;
            }
            // Stopped, and only for this run: with every process seen gone, the same name in front is a new one.
            if (_pids.Any(isRunning))
            {
                _declined = true;
                if (inFront) _pids.Add(frontPid);
            }
            else
            {
                Forget();
            }
        }
        if (frontPid == 0 || !asked) return AutoSessionStep.None;
        var same = _game is not null && string.Equals(frontGame, _game, StringComparison.OrdinalIgnoreCase);
        if (sessionRunning)
        {
            // By hand, with this game on: the person is in charge of its session from here.
            if (_game is null) Remember(frontPid, frontGame, declined: true);
            return AutoSessionStep.None;
        }
        if (same || !canStart) return AutoSessionStep.None;
        Remember(frontPid, frontGame, declined: false);
        return AutoSessionStep.Start;
    }

    private void Remember(uint pid, string? game, bool declined)
    {
        _pids.Clear();
        _pids.Add(pid);
        _game = game ?? "";
        _declined = declined;
    }

    private void Forget()
    {
        _pids.Clear();
        _game = null;
        _declined = false;
    }
}
