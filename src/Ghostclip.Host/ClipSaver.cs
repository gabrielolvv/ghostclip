using System.Diagnostics;
using System.Globalization;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>A clip frozen at the moment of the hotkey, waiting to become an MP4.</summary>
internal sealed record ClipSnapshot(string Dir, string Game, DateTime Time, int Segments, double Seconds, int AudioLeadMs, TimeSpan Took);

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
    private const string ListFile = "list.ffconcat";
    private const string MetaFile = "clip.txt";
    private readonly SemaphoreSlim _remuxGate = new(1, 1);

    public ClipSnapshot TakeSnapshot(string game, bool recording)
    {
        var sw = Stopwatch.StartNew();
        var now = DateTime.Now;
        var cfg = config();
        var plan = ClipPlanner.Plan(SegmentRing.Scan(paths.BufferDir), cfg.ClipSeconds, GhostclipConfig.SegmentSeconds, recording);
        if (plan.IsEmpty)
        {
            throw new InvalidOperationException("Nothing recorded yet: the replay buffer is empty.");
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
            throw new InvalidOperationException("The buffer segments disappeared before they could be saved.");
        }

        // The newest segment's session decides the audio shift: that's the part people watch.
        var newest = plan.Current ?? plan.Complete[^1];
        var lead = AudioSync.ForSequence(AudioSync.Scan(paths.BufferDir), newest.Sequence)?.AudioLeadMs ?? 0;

        File.WriteAllText(Path.Combine(dir, ListFile), FfmpegArgs.ConcatList(names));
        File.WriteAllLines(Path.Combine(dir, MetaFile),
            [game, now.ToString("o", CultureInfo.InvariantCulture), lead.ToString(CultureInfo.InvariantCulture)]);
        var snapshot = new ClipSnapshot(dir, game, now, names.Count, plan.EstimatedSeconds, lead, sw.Elapsed);
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
                ?? throw new InvalidOperationException("FFmpeg was not found.");
            var output = ClipNaming.BuildPath(cfg.ResolvedClipsFolder, snapshot.Game, snapshot.Time, File.Exists);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            var result = await ProcessRunner.RunAsync(ffmpeg, FfmpegArgs.Remux(Path.Combine(snapshot.Dir, ListFile), output, snapshot.AudioLeadMs),
                TimeSpan.FromSeconds(120), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                TryDelete(output);
                // The snapshot stays in Pending and is retried on the next start.
                throw new InvalidOperationException($"Could not write the clip: {result.FirstError}");
            }
            Directory.Delete(snapshot.Dir, recursive: true);
            log.Information("Clip saved: {Path} (remux {Ms} ms)", output, (long)result.Elapsed.TotalMilliseconds);
            return output;
        }
        finally
        {
            _remuxGate.Release();
        }
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
                // Interrupted mid-snapshot: nothing usable.
                Directory.Delete(dir, recursive: true);
                continue;
            }
            var lines = File.ReadAllLines(meta);
            var time = lines.Length > 1 && DateTime.TryParse(lines[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : Directory.GetCreationTime(dir);
            var segments = Directory.GetFiles(dir, "seg_*.ts").Length;
            var lead = lines.Length > 2 && int.TryParse(lines[2], CultureInfo.InvariantCulture, out var l) ? l : 0;
            result.Add(new ClipSnapshot(dir, lines.Length > 0 ? lines[0] : ClipNaming.FallbackGame, time, segments,
                segments * GhostclipConfig.SegmentSeconds, lead, TimeSpan.Zero));
        }
        return result;
    }

    private bool LinkOrCopy(string source, string target)
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
    private bool CopyGrowing(string source, string target)
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

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
