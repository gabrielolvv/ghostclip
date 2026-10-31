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
/// </summary>
internal sealed class SessionRecorder : IDisposable
{
    private readonly GhostclipPaths _paths;
    private readonly ClipSaver _saver;
    private readonly ILogger _log;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly SortedSet<long> _linked = [];
    private readonly List<double> _bookmarks = [];
    private string? _dir;
    private string _game = ClipNaming.FallbackGame;
    private long _from;
    private DateTime _startedLocal;
    private DateTime? _originUtc;
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

    public bool Running => _dir is not null;

    public string Game => _game;

    public TimeSpan Elapsed => Running ? DateTime.Now - _startedLocal : TimeSpan.Zero;

    /// <summary>Once a second while a session runs, on the UI thread: for the tray's running time.</summary>
    public event Action? Ticked;

    public void Start(string game)
    {
        if (Running) return;
        // From the segment being written now: the session starts at most 2 s early, never late.
        var segments = SegmentRing.Scan(_paths.BufferDir);
        _from = segments.Count > 0 ? segments[^1].Sequence : 0;
        _game = game;
        _startedLocal = DateTime.Now;
        // The joined file starts where that segment does: bookmarks count from there.
        _originUtc = segments.Count > 0 ? File.GetCreationTimeUtc(segments[^1].Path) : null;
        _audio = null;
        _linked.Clear();
        _bookmarks.Clear();
        _dir = Path.Combine(_paths.PendingDir, _startedLocal.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-session");
        Directory.CreateDirectory(_dir);
        _timer.Start();
        _log.Information("Session recording started for {Game} from segment {From}", game, _from);
    }

    /// <summary>Marks this moment; the library shows it on the session's timeline.</summary>
    public void AddBookmark()
    {
        if (_dir is null) return;
        Collect();
        var at = _originUtc is { } origin ? Math.Max(0, (DateTime.UtcNow - origin).TotalSeconds) : 0;
        _bookmarks.Add(Math.Round(at, 1));
        File.WriteAllLines(Path.Combine(_dir, ClipSaver.BookmarksFile), _bookmarks.Select(b => b.ToString(CultureInfo.InvariantCulture)));
        _log.Information("Bookmark at {Seconds:0.0} s", at);
    }

    /// <summary>Stops collecting and hands the session over to be joined; null when nothing was recorded.</summary>
    public ClipSnapshot? Stop()
    {
        if (_dir is not { } dir) return null;
        _timer.Stop();
        Collect(final: true);
        _dir = null;
        var took = DateTime.Now - _startedLocal;
        _log.Information("Session recording stopped after {Minutes:0.0} min: {Segments} segments, {Bookmarks} bookmarks",
            took.TotalMinutes, _linked.Count, _bookmarks.Count);
        if (_linked.Count == 0)
        {
            Directory.Delete(dir, recursive: true);
            return null;
        }
        return new ClipSnapshot(dir, _game, _startedLocal, _linked.Count, _linked.Count * GhostclipConfig.SegmentSeconds,
            _audio?.AudioLeadMs ?? 0, _audio?.AudioTitles, took, _bookmarks.ToList());
    }

    // Links every closed segment of the session and rewrites the list and details to match. The one
    // being written waits until it closes (a copy of it now would stay cut short), except at the end,
    // where up to the last moment is what's wanted.
    private void Collect(bool final = false)
    {
        if (_dir is not { } dir) return;
        var added = false;
        var segments = SegmentRing.Scan(_paths.BufferDir);
        var writing = segments.Count > 0 ? segments[^1].Sequence : -1;
        foreach (var segment in segments)
        {
            if (segment.Sequence < _from || _linked.Contains(segment.Sequence)) continue;
            if (segment.Sequence == writing && !final) continue;
            var name = Path.GetFileName(segment.Path);
            if (!_saver.LinkOrCopy(segment.Path, Path.Combine(dir, name))) continue;
            _linked.Add(segment.Sequence);
            _originUtc ??= File.GetCreationTimeUtc(segment.Path);
            added = true;
        }
        // The audio shift is measured a moment after recording starts; read it while the buffer still has it.
        _audio ??= AudioSync.ForSequence(AudioSync.Scan(_paths.BufferDir), _from);
        if (!added) return;
        File.WriteAllText(Path.Combine(dir, ClipSaver.ListFile),
            FfmpegArgs.ConcatList(_linked.Select(SegmentRing.FileName)));
        File.WriteAllLines(Path.Combine(dir, ClipSaver.MetaFile),
        [
            _game, _startedLocal.ToString("o", CultureInfo.InvariantCulture),
            (_audio?.AudioLeadMs ?? 0).ToString(CultureInfo.InvariantCulture), string.Join('|', _audio?.AudioTitles ?? []),
        ]);
    }

    public void Dispose() => _timer.Dispose();
}
