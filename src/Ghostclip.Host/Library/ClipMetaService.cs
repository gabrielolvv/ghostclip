using System.Diagnostics;
using System.Text.Json;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host.Library;

/// <summary>
/// A clip's details, waveform peaks, keyframes and thumbnail, made once and
/// cached in Data\Thumbs under a key that changes with the file. Only for
/// clips the page shows, two at a time, at below-normal priority.
/// </summary>
internal sealed class ClipMetaService(GhostclipPaths paths, ClipTools tools, ILogger log)
{
    private readonly SemaphoreSlim _gate = new(2);
    private readonly SemaphoreSlim _trackGate = new(1);
    // Tracks being taken out, by clip, to stop when the clip is let go of or about to move: FFmpeg holds it open.
    private readonly Dictionary<string, List<TrackRun>> _trackRuns = new(StringComparer.OrdinalIgnoreCase);

    public string ThumbsDir { get; } = Path.Combine(paths.DataDir, "Thumbs");

    /// <param name="coverAt">The second people picked for its card; null for the automatic one.</param>
    public async Task<(ClipMeta Meta, string? ThumbFile)> GetAsync(string clip, CancellationToken ct, double? coverAt = null)
    {
        var info = new FileInfo(clip);
        var key = ClipMedia.CacheKey(clip, info.Length, info.LastWriteTimeUtc);
        var metaFile = Path.Combine(ThumbsDir, key + ".json");
        var thumb = Path.Combine(ThumbsDir, ClipMedia.ThumbName(key, coverAt));
        var meta = ReadCached(metaFile);
        if (meta is not null && File.Exists(thumb))
        {
            // Seen again: kept out of the cache pruning for another 90 days.
            ClipMedia.Touch(metaFile, DateTime.UtcNow);
            ClipMedia.Touch(thumb, DateTime.UtcNow);
            return (meta, thumb);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ThumbsDir);
            if (meta is null)
            {
                var probe = await tools.ProbeAsync(clip, ct).ConfigureAwait(false);
                var keyframes = await ProcessRunner.RunAsync(tools.Ffprobe(), FfmpegArgs.Keyframes(clip),
                    TimeSpan.FromSeconds(60), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
                ProcessResult? peaks = null;
                if (probe.AudioTracks > 0)
                {
                    // astats prints at info level, to stderr.
                    peaks = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.AudioPeaks(clip, ClipMedia.SamplesPerPeak(probe.Duration)),
                        TimeSpan.FromSeconds(60), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
                }
                // Throws on a failed or timed-out probe, so nothing partial is cached.
                meta = ClipMedia.BuildMeta(probe.Duration, probe.Height, probe.Fps, probe.AudioTracks, probe.VideoCodec, keyframes, peaks,
                    probe.AudioTitles.Count > 0 ? probe.AudioTitles : null);
                await WriteAtomicallyAsync(metaFile, JsonSerializer.Serialize(meta, LibraryJson.Default.ClipMeta), ct).ConfigureAwait(false);
            }

            // A clip works without a thumbnail; a failed one is tried again the next time it is asked for.
            var part = $"{thumb}.{Guid.NewGuid():N}.part";
            ProcessResult shot;
            try
            {
                shot = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.Thumbnail(clip, part, coverAt is { } at ? FfmpegArgs.FrameSeek(at, meta.Fps) : meta.Duration / 3),
                    TimeSpan.FromSeconds(30), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryDelete(part);
                throw;
            }
            if (!shot.Succeeded || !File.Exists(part))
            {
                log.Warning("No thumbnail for {Clip}: {Error}", clip, shot.TimedOut ? "timed out" : shot.FirstError);
                TryDelete(part);
                return (meta, null);
            }
            File.Move(part, thumb, overwrite: true);
            return (meta, thumb);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// A clip's audio track <paramref name="track"/> on its own, made once by stream copy and cached beside its details,
    /// for the player to play beside the video, which plays the first track only.
    /// </summary>
    public async Task<string> TrackFileAsync(string clip, int track, CancellationToken ct)
    {
        var info = new FileInfo(clip);
        var key = ClipMedia.CacheKey(clip, info.Length, info.LastWriteTimeUtc);
        var file = Path.Combine(ThumbsDir, ClipMedia.TrackName(key, track));
        if (File.Exists(file))
        {
            ClipMedia.Touch(file, DateTime.UtcNow);
            return file;
        }

        var run = new TrackRun();
        lock (_trackRuns)
        {
            if (!_trackRuns.TryGetValue(clip, out var runs)) _trackRuns[clip] = runs = [];
            runs.Add(run);
        }
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, run.Stop.Token);
            return await ExtractTrackAsync(clip, track, file, stop.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_trackRuns)
            {
                if (_trackRuns.TryGetValue(clip, out var runs) && runs.Remove(run) && runs.Count == 0) _trackRuns.Remove(clip);
            }
            run.Done.TrySetResult();
        }
    }

    /// <summary>
    /// Stops the tracks being taken out of these clips and waits for FFmpeg to let go of them: for a clip about to be
    /// deleted, renamed or moved, or that the player left before its tracks were ready.
    /// </summary>
    public async Task StopTracksAsync(IEnumerable<string> clips)
    {
        var runs = new List<TrackRun>();
        lock (_trackRuns)
        {
            foreach (var clip in clips)
            {
                if (_trackRuns.Remove(clip, out var those)) runs.AddRange(those);
            }
        }
        if (runs.Count == 0) return;
        foreach (var run in runs) run.Stop.Cancel();
        try
        {
            // FFmpeg is killed and waited for (ProcessRunner): once each has ended, the file is free.
            await Task.WhenAll(runs.Select(r => r.Done.Task)).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            log.Warning("A track for the player was still being taken out after 10 s");
        }
    }

    // One at a time, and on a gate of their own: a session's tracks take a while, and the cards' details mustn't wait
    // for them. A second request for the same track waits here and finds it made.
    private async Task<string> ExtractTrackAsync(string clip, int track, string file, CancellationToken ct)
    {
        await _trackGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(file))
            {
                ClipMedia.Touch(file, DateTime.UtcNow);
                return file;
            }
            Directory.CreateDirectory(ThumbsDir);
            var part = $"{file}.{Guid.NewGuid():N}.part";
            ProcessResult r;
            try
            {
                // About the time it takes to read the file: a session of hours on a slow disk takes a while.
                r = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.ExtractTrack(clip, part, track),
                    TimeSpan.FromMinutes(10), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryDelete(part);
                throw;
            }
            if (!r.Succeeded || !File.Exists(part))
            {
                TryDelete(part);
                throw new InvalidOperationException($"Track {track} of {Path.GetFileName(clip)}: {(r.TimedOut ? "timed out" : r.FirstError)}");
            }
            File.Move(part, file, overwrite: true);
            log.Information("Track {Track} of {Clip} for the player: {File}", track, clip, file);
            return file;
        }
        finally
        {
            _trackGate.Release();
        }
    }

    // Not disposed: StopTracksAsync may cancel one just as it ends.
    private sealed class TrackRun
    {
        public CancellationTokenSource Stop { get; } = new();
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static ClipMeta? ReadCached(string metaFile)
    {
        try
        {
            return File.Exists(metaFile) ? JsonSerializer.Deserialize(File.ReadAllText(metaFile), LibraryJson.Default.ClipMeta) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    // Two requests for the same clip can race; each writes its own temporary file and the last move wins.
    private static async Task WriteAtomicallyAsync(string path, string text, CancellationToken ct)
    {
        var temp = $"{path}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
