namespace Ghostclip.Core;

/// <summary>Whether recording is paused, and a task to wait for that to change. One writer: the pipe; one reader: the recorder loop.</summary>
public sealed class PauseSwitch
{
    private readonly Lock _lock = new();
    private bool _paused;
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

    public void Set(bool paused)
    {
        TaskCompletionSource changed;
        lock (_lock)
        {
            if (_paused == paused) return;
            _paused = paused;
            changed = _changed;
            _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        changed.SetResult();
    }
}
