namespace Ghostclip.Core;

/// <summary>Whether recording is paused, and a task to wait for that to change. One writer: the pipe; one reader: the recorder loop.</summary>
public sealed class PauseSwitch
{
    private readonly Lock _lock = new();
    private bool _paused;
    private bool _clearBuffer;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Paused
    {
        get { lock (_lock) return _paused; }
    }

    /// <summary>Completes at the next change. Read it before <see cref="Paused"/>, so a change between the two can't go unnoticed.</summary>
    public Task Changed
    {
        get { lock (_lock) return _changed.Task; }
    }

    /// <summary>
    /// The recorder has to stop: it is paused, or was since the buffer was last cleared. A pause and a resume in a row,
    /// to get rid of what was just on screen, can both land before the recorder loop looks.
    /// </summary>
    public bool MustStop
    {
        get { lock (_lock) return _paused || _clearBuffer; }
    }

    /// <summary>Whether a pause still wants the buffer cleared, and forgets it: take it before clearing, so a later pause asks again.</summary>
    public bool TakeBufferClear()
    {
        lock (_lock)
        {
            var clear = _clearBuffer;
            _clearBuffer = false;
            return clear;
        }
    }

    public void Set(bool paused)
    {
        TaskCompletionSource changed;
        lock (_lock)
        {
            if (_paused == paused) return;
            _paused = paused;
            _clearBuffer |= paused;
            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        changed.SetResult();
    }
}
