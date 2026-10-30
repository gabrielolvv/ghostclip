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

    public string ThumbsDir { get; } = Path.Combine(paths.DataDir, "Thumbs");

    public async Task<(ClipMeta Meta, string? ThumbFile)> GetAsync(string clip, CancellationToken ct)
    {
        var info = new FileInfo(clip);
        var key = ClipMedia.CacheKey(clip, info.Length, info.LastWriteTimeUtc);
        var metaFile = Path.Combine(ThumbsDir, key + ".json");
        var thumb = Path.Combine(ThumbsDir, key + ".jpg");
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
                var probe = await tools.ProbeAsync(clip).ConfigureAwait(false);
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
                meta = ClipMedia.BuildMeta(probe.Duration, probe.Height, probe.Fps, probe.AudioTracks, probe.VideoCodec, keyframes, peaks);
                await WriteAtomicallyAsync(metaFile, JsonSerializer.Serialize(meta, LibraryJson.Default.ClipMeta), ct).ConfigureAwait(false);
            }

            // A clip works without a thumbnail; a failed one is tried again the next time it is asked for.
            var part = $"{thumb}.{Guid.NewGuid():N}.part";
            var shot = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.Thumbnail(clip, part, meta.Duration / 3),
                TimeSpan.FromSeconds(30), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
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
        await File.WriteAllTextAsync(temp, text, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
