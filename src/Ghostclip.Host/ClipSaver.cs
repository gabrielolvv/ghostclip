using System.Diagnostics;
using System.Globalization;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>A clip frozen at the moment of the hotkey (or a session when it stopped), waiting to become an MP4.</summary>
/// <param name="Bookmarks">Seconds into the clip that a session marked; the library shows them on the timeline.</param>
internal sealed record ClipSnapshot(string Dir, string Game, DateTime Time, int Segments, double Seconds, int AudioLeadMs, string[]? AudioTitles, TimeSpan Took,
    IReadOnlyList<double>? Bookmarks = null);

/// <summary>
/// Saving happens in two steps.
///
/// 1. Snapshot, on the hotkey: closed segments are hard-linked into a pending
///    folder (no data is copied, so it takes milliseconds whatever the disk) and
///    only the segment still being written is copied. From here the clip is
///    safe: rotation can't delete it and a crash doesn't lose it.
/// 2. Remux, right after, at below-normal priority: the snapshot is joined by
///    stream copy into the final MP4 and the pending folder is removed.
///
/// The host does this, not the worker, so a clip can be saved even while the
/// worker is restarting.
/// </summary>
internal sealed class ClipSaver(GhostclipPaths paths, Func<GhostclipConfig> config, ILogger log)
{
    internal const string ListFile = "list.ffconcat";
    internal const string MetaFile = "clip.txt";
    internal const string BookmarksFile = "bookmarks.txt";
    private readonly SemaphoreSlim _remuxGate = new(1, 1);

    public ClipSnapshot TakeSnapshot(string game, bool recording)
    {
        var sw = Stopwatch.StartNew();
        var now = DateTime.Now;
        var cfg = config();
        var plan = ClipPlanner.Plan(SegmentRing.Scan(paths.BufferDir), cfg.ClipSecondsFor(game), GhostclipConfig.SegmentSeconds, recording);
        if (plan.IsEmpty)
        {
            throw new InvalidOperationException(L("Nothing recorded yet: the replay buffer is empty.", "Nada gravado ainda: o buffer de replay está vazio."));
        }

        var dir = Path.Combine(paths.PendingDir, now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var names = new List<string>();
        foreach (var segment in plan.Complete)
        {
            var name = Path.GetFileName(segment.Path);
            if (LinkOrCopy(segment.Path, Path.Combine(dir, name))) names.Add(name);
        }
        if (plan.Current is { } current)
        {
            var name = Path.GetFileName(current.Path);
            if (CopyGrowing(current.Path, Path.Combine(dir, name))) names.Add(name);
        }
        if (names.Count == 0)
        {
            Directory.Delete(dir, recursive: true);
            throw new InvalidOperationException(L("The buffer segments disappeared before they could be saved.", "Os segmentos do buffer sumiram antes de serem salvos."));
        }

        // The newest segment's session decides the audio shift: that's the part people watch.
        var newest = plan.Current ?? plan.Complete[^1];
        var session = AudioSync.ForSequence(AudioSync.Scan(paths.BufferDir), newest.Sequence);
        var lead = session?.AudioLeadMs ?? 0;
        var titles = session?.AudioTitles;

        File.WriteAllText(Path.Combine(dir, ListFile), FfmpegArgs.ConcatList(names));
        File.WriteAllLines(Path.Combine(dir, MetaFile),
            [game, now.ToString("o", CultureInfo.InvariantCulture), lead.ToString(CultureInfo.InvariantCulture), string.Join('|', titles ?? [])]);
        var snapshot = new ClipSnapshot(dir, game, now, names.Count, plan.EstimatedSeconds, lead, titles, sw.Elapsed);
        log.Information("Snapshot of {Segments} segments (~{Seconds:0} s) for {Game} in {Ms} ms",
            names.Count, plan.EstimatedSeconds, game, sw.ElapsedMilliseconds);
        return snapshot;
    }

    public async Task<string> RemuxAsync(ClipSnapshot snapshot, CancellationToken ct = default)
    {
        await _remuxGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cfg = config();
            var ffmpeg = FfmpegLocator.Find(cfg.FfmpegPath, AppContext.BaseDirectory)
                ?? throw new InvalidOperationException(L("FFmpeg was not found.", "O FFmpeg não foi encontrado."));
            var output = ClipNaming.BuildPath(cfg.ResolvedClipsFolder, snapshot.Game, snapshot.Time, File.Exists);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            EnsureRoomFor(snapshot.Dir, output);

            // Written under a temporary name, so the library never lists a half-written clip.
            // Stream copy runs far faster than real time; a long session gets a second per segment.
            var timeout = TimeSpan.FromSeconds(Math.Max(120, snapshot.Segments));
            var result = await ProcessRunner.RunAsync(ffmpeg, FfmpegArgs.Remux(Path.Combine(snapshot.Dir, ListFile), PartFile.For(output), snapshot.AudioLeadMs, snapshot.AudioTitles),
                timeout, ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                PartFile.Discard(output);
                // The snapshot stays in Pending and is retried on the next start.
                throw new InvalidOperationException(L($"Could not write the clip: {result.FirstError}", $"Não foi possível gravar o clipe: {result.FirstError}"));
            }
            PartFile.Commit(output);
            if (snapshot.Bookmarks is { Count: > 0 } bookmarks)
            {
                // The clip is saved by now; its bookmarks are a convenience and must not fail it.
                try
                {
                    LibraryStore.Update(paths.LibraryFile, s => s.SetBookmarks(output, bookmarks));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.Warning("Saved {Path} without its bookmarks: {Error}", output, ex.Message);
                }
            }
            // Saved. Without its details the pending folder is a leftover, not a clip to save again on the
            // next start, even if a file in it can't be deleted yet.
            try
            {
                File.Delete(Path.Combine(snapshot.Dir, MetaFile));
                Directory.Delete(snapshot.Dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning("Saved {Path}; its pending folder is removed on the next start: {Error}", output, ex.Message);
            }
            log.Information("Clip saved: {Path} (remux {Ms} ms)", output, (long)result.Elapsed.TotalMilliseconds);
            return output;
        }
        finally
        {
            _remuxGate.Release();
        }
    }

    // The joined file is about the size of its segments, written while they still take their space. Said
    // plainly when it doesn't fit, instead of an FFmpeg write error; the snapshot stays for the next try.
    private static void EnsureRoomFor(string snapshotDir, string output)
    {
        long needed = 0;
        foreach (var segment in Directory.EnumerateFiles(snapshotDir, "seg_*.ts")) needed += new FileInfo(segment).Length;
        var root = Path.GetPathRoot(Path.GetFullPath(output));
        if (string.IsNullOrEmpty(root)) return;
        long free;
        try
        {
            free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }
        const long margin = 64L * 1024 * 1024;
        if (free >= needed + margin) return;
        var gb = (needed + margin) / 1024.0 / 1024 / 1024;
        throw new IOException(L($"Not enough free space on {root}: saving it needs about {gb:0.0} GB. It is kept and saved on the next start.",
            $"Não há espaço livre suficiente em {root}: salvar precisa de cerca de {gb:0.0} GB. Ele fica guardado e é salvo na próxima vez que o Ghostclip abrir."));
    }

    /// <summary>Snapshots left by a run that ended before remuxing them.</summary>
    public IReadOnlyList<ClipSnapshot> PendingFromEarlierRuns()
    {
        var result = new List<ClipSnapshot>();
        if (!Directory.Exists(paths.PendingDir)) return result;
        foreach (var dir in Directory.EnumerateDirectories(paths.PendingDir))
        {
            var meta = Path.Combine(dir, MetaFile);
            if (!File.Exists(meta) || !File.Exists(Path.Combine(dir, ListFile)))
            {
                // Interrupted mid-snapshot, or saved already: nothing to do but clear it away.
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Debug("Could not remove {Dir}: {Error}", dir, ex.Message); }
                continue;
            }
            var lines = File.ReadAllLines(meta);
            var time = lines.Length > 1 && DateTime.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : Directory.GetCreationTime(dir);
            var segments = Directory.GetFiles(dir, "seg_*.ts").Length;
            var lead = lines.Length > 2 && int.TryParse(lines[2], CultureInfo.InvariantCulture, out var l) ? l : 0;
            var titles = lines.Length > 3 && lines[3].Length > 0 ? lines[3].Split('|') : null;
            result.Add(new ClipSnapshot(dir, lines.Length > 0 ? lines[0] : ClipNaming.FallbackGame, time, segments,
                segments * GhostclipConfig.SegmentSeconds, lead, titles, TimeSpan.Zero, ReadBookmarks(dir)));
        }
        return result;
    }

    // A session's bookmarks, one offset in seconds per line; a crash mid-session keeps them.
    internal static IReadOnlyList<double>? ReadBookmarks(string dir)
    {
        var file = Path.Combine(dir, BookmarksFile);
        if (!File.Exists(file)) return null;
        return File.ReadAllLines(file)
            .Select(l => double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : -1)
            .Where(s => s >= 0).ToList();
    }

    internal bool LinkOrCopy(string source, string target)
    {
        if (Native.CreateHardLink(target, source, 0)) return true;
        try
        {
            File.Copy(source, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rotation deleted it between the scan and now: it was the oldest one.
            log.Debug("Skipping {Segment}: {Error}", source, ex.Message);
            return false;
        }
    }

    // Copies the bytes written so far. The recorder keeps writing to its file.
    internal bool CopyGrowing(string source, string target)
    {
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = input.Length;
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            var buffer = new byte[256 * 1024];
            long copied = 0;
            while (copied < length)
            {
                var n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length - copied));
                if (n == 0) break;
                output.Write(buffer, 0, n);
                copied += n;
            }
            return copied > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Debug("Skipping current segment {Segment}: {Error}", source, ex.Message);
            return false;
        }
    }
}
