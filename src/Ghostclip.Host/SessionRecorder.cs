using System.Globalization;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>
/// Records a whole session without a second encoder. The replay buffer is
/// already being written, so a session only keeps it: every segment from the
/// start on is hard-linked into a pending folder as soon as it appears, and
/// rotation then removes just the buffer's name for it. Stopping joins them by
/// stream copy, like a clip, so the game pays nothing extra; the disk does
/// (about 9 GB an hour at 20 Mbps).
///
/// The pending folder is a snapshot from the first second: list, details and
/// bookmarks are kept up to date in it, so a crash or an exit mid-session is
/// finished into an MP4 on the next start, like any other pending clip.
///
/// When the recorder stops mid-session (a crash, a locked screen), the part so
/// far is handed over to be saved and the session goes on in a new part once
/// recording resumes (<see cref="SessionPlan"/>): segments of two runs may not
/// join, and the gap would throw every later bookmark off.
/// </summary>
internal sealed class SessionRecorder : IDisposable
{
    private readonly GhostclipPaths _paths;
    private readonly ClipSaver _saver;
    private readonly ILogger _log;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly SortedSet<long> _linked = [];
    private readonly List<double> _bookmarks = [];
    private bool _running;
    private string? _dir;
    private string _game = ClipNaming.FallbackGame;
    private long _from;
    private DateTime? _resumedAfterUtc;
    private long? _runStart;
    private DateTime _startedLocal;
    private DateTime _partStartedLocal;
    private RecordingSession? _audio;

    public SessionRecorder(GhostclipPaths paths, ClipSaver saver, ILogger log)
    {
        _paths = paths;
        _saver = saver;
        _log = log;
        _timer.Tick += (_, _) =>
        {
            Collect();
            Ticked?.Invoke();
        };
    }

    public bool Running => _running;

    public string Game => _game;

    public TimeSpan Elapsed => Running ? DateTime.Now - _startedLocal : TimeSpan.Zero;

    /// <summary>Parts already handed over this session: the recorder stopped in between.</summary>
    public int SavedParts { get; private set; }

    /// <summary>Once a second while a session runs, on the UI thread: for the tray's running time.</summary>
    public event Action? Ticked;

    public void Start(string game)
    {
        if (Running) return;
        _running = true;
        _game = game;
        _startedLocal = DateTime.Now;
        SavedParts = 0;
        // From the segment being written now: the session starts at most 2 s early, never late.
        var segments = SegmentRing.Scan(_paths.BufferDir);
        BeginPart(segments.Count > 0 ? segments[^1].Sequence : 0, resumedAfterUtc: null);
        _timer.Start();
        _log.Information("Session recording started for {Game} from segment {From}", game, _from);
    }

    /// <summary>Marks this moment; the library shows it on the session's timeline.</summary>
    public void AddBookmark()
    {
        if (_dir is not { } dir) return;
        Collect();
        // Only segments of this part's run are in it, back to back: counted, not timed from file dates.
        var segments = SegmentRing.Scan(_paths.BufferDir);
        var at = 0.0;
        if (_from >= 0 && segments.Count > 0)
        {
            var writing = segments[^1].Sequence;
            DateTime? begun = segments.Count > 1 && segments[^2].Sequence == writing - 1 ? segments[^2].LastWriteUtc : null;
            at = SessionPlan.OffsetAt(_from, writing, begun, DateTime.UtcNow, GhostclipConfig.SegmentSeconds);
        }
        _bookmarks.Add(Math.Round(at, 1));
        // A full disk must not cost the clip being saved along with the bookmark.
        TryWrite(() => File.WriteAllLines(Path.Combine(dir, ClipSaver.BookmarksFile), _bookmarks.Select(b => b.ToString(CultureInfo.InvariantCulture))));
        _log.Information("Bookmark at {Seconds:0.0} s", at);
    }

    /// <summary>
    /// The recorder stopped: hands over the part so far to be saved, null when it holds nothing.
    /// The session goes on, waiting for <see cref="RecorderResumed"/>. <paramref name="nextRunStart"/>,
    /// when the next run is already recording, is where this part ends.
    /// </summary>
    public ClipSnapshot? RecorderStopped(long? nextRunStart = null)
    {
        if (!Running || _dir is null) return null;
        var part = EndPart(nextRunStart ?? long.MaxValue);
        _resumedAfterUtc = DateTime.UtcNow;
        _log.Information("Recorder stopped mid-session; the session continues in a new part when recording resumes");
        if (part is not null) SavedParts++;
        return part;
    }

    /// <summary>
    /// Recording is back: a new part takes the segments of the new run, from <paramref name="runStart"/>
    /// when the recorder says where it started, or else the ones created since it stopped.
    /// </summary>
    public void RecorderResumed(long? runStart = null)
    {
        if (!Running || _dir is not null || _resumedAfterUtc is not { } after) return;
        // A stop seen late (the tray busy for a second) can have let the last part take this run's first
        // segments: this one starts after them, never on a segment already saved.
        var from = runStart ?? -1;
        if (runStart is { } start && _linked.Contains(start)) from = _linked.Max + 1;
        BeginPart(from, resumedAfterUtc: after, runStart);
        _log.Information("Session continues in a new part");
    }

    /// <summary>Stops collecting and hands the session (its last part) over to be joined; null when nothing was recorded.</summary>
    public ClipSnapshot? Stop()
    {
        if (!Running) return null;
        _timer.Stop();
        var part = _dir is null ? null : EndPart(long.MaxValue);
        _running = false;
        var took = DateTime.Now - _startedLocal;
        _log.Information("Session recording stopped after {Minutes:0.0} min ({Parts} earlier parts)", took.TotalMinutes, SavedParts);
        return part;
    }

    private void BeginPart(long from, DateTime? resumedAfterUtc, long? runStart = null)
    {
        _runStart = runStart;
        _from = from;
        _resumedAfterUtc = resumedAfterUtc;
        _partStartedLocal = DateTime.Now;
        _audio = null;
        _linked.Clear();
        _bookmarks.Clear();
        _dir = Path.Combine(_paths.PendingDir, _partStartedLocal.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-session");
        Directory.CreateDirectory(_dir);
    }

    private ClipSnapshot? EndPart(long before)
    {
        var dir = _dir!;
        Collect(final: true, before);
        _dir = null;
        var took = DateTime.Now - _partStartedLocal;
        _log.Information("Session part ended after {Minutes:0.0} min: {Segments} segments, {Bookmarks} bookmarks",
            took.TotalMinutes, _linked.Count, _bookmarks.Count);
        if (_linked.Count == 0)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Debug("Could not remove {Dir}: {Error}", dir, ex.Message); }
            return null;
        }
        return new ClipSnapshot(dir, _game, _partStartedLocal, _linked.Count, _linked.Count * GhostclipConfig.SegmentSeconds,
            _audio?.AudioLeadMs ?? 0, _audio?.AudioTitles, took, _bookmarks.ToList());
    }

    // Links every closed segment of this part and rewrites the list and details to match. The one being
    // written waits until it closes (a link to it now would be a file the recorder still holds), except at
    // the end, where it is copied as far as it goes.
    private void Collect(bool final = false, long before = long.MaxValue)
    {
        if (_dir is not { } dir) return;
        var segments = SegmentRing.Scan(_paths.BufferDir);
        if (_from < 0)
        {
            // A part after a restart: its run's segments are the ones created since, whatever their numbers.
            if (_resumedAfterUtc is not { } after) return;
            var first = SessionPlan.FirstAfter(segments.Select(s => (s.Sequence, File.GetCreationTimeUtc(s.Path))), after);
            if (first is null) return;
            _from = first.Value;
        }
        var writing = segments.Count > 0 ? segments[^1].Sequence : -1;
        var added = false;
        foreach (var sequence in SessionPlan.ToLink(segments.Select(s => s.Sequence).ToList(), _from, _linked, final, before))
        {
            var segment = segments.First(s => s.Sequence == sequence);
            var target = Path.Combine(dir, Path.GetFileName(segment.Path));
            var ok = sequence == writing ? _saver.CopyGrowing(segment.Path, target) : _saver.LinkOrCopy(segment.Path, target);
            if (!ok) continue;
            _linked.Add(sequence);
            added = true;
        }
        // The audio shift is measured a moment after recording starts; read it while the buffer still has it.
        // Once a segment is in: the run's own measurement, which its recorder writes a moment after starting.
        // With the run known, an earlier run's is not taken for it.
        if (_audio is null && _linked.Count > 0)
        {
            var audio = AudioSync.ForSequence(AudioSync.Scan(_paths.BufferDir), _from);
            if (_runStart is not { } run || audio?.StartSequence == run) _audio = audio;
        }
        if (!added) return;
        TryWrite(() =>
        {
            File.WriteAllText(Path.Combine(dir, ClipSaver.ListFile), FfmpegArgs.ConcatList(_linked.Select(SegmentRing.FileName)));
            File.WriteAllLines(Path.Combine(dir, ClipSaver.MetaFile),
            [
                _game, _partStartedLocal.ToString("o", CultureInfo.InvariantCulture),
                (_audio?.AudioLeadMs ?? 0).ToString(CultureInfo.InvariantCulture), string.Join('|', _audio?.AudioTitles ?? []),
            ]);
        });
    }

    // The details are rewritten every second: a write that fails (a full disk) is logged and tried again
    // with the next segment, never thrown at the tray, where it would stop an exit half way.
    private void TryWrite(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Could not update the session's details: {Error}", ex.Message);
        }
    }

    public void Dispose() => _timer.Dispose();
}
