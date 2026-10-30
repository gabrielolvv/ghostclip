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
        if (ReadCached(metaFile) is { } cached) return (cached, File.Exists(thumb) ? thumb : null);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ThumbsDir);
            var probe = await tools.ProbeAsync(clip).ConfigureAwait(false);
            var keyframes = await ProcessRunner.RunAsync(tools.Ffprobe(), FfmpegArgs.Keyframes(clip),
                TimeSpan.FromSeconds(60), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
            IReadOnlyList<float> peaks = [];
            if (probe.AudioTracks > 0)
            {
                // astats prints at info level, to stderr.
                var r = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.AudioPeaks(clip, ClipMedia.SamplesPerPeak(probe.Duration)),
                    TimeSpan.FromSeconds(60), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
                peaks = ClipMedia.ParsePeaks(r.StdErr);
            }
            var shot = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.Thumbnail(clip, thumb, probe.Duration / 3),
                TimeSpan.FromSeconds(30), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
            if (!shot.Succeeded) log.Warning("No thumbnail for {Clip}: {Error}", clip, shot.FirstError);

            var meta = new ClipMeta(probe.Duration, probe.Height, Math.Round(probe.Fps, 2), probe.AudioTracks, probe.VideoCodec,
                ClipMedia.ParseKeyframes(keyframes.StdOut), peaks);
            await File.WriteAllTextAsync(metaFile, JsonSerializer.Serialize(meta, LibraryJson.Default.ClipMeta), ct).ConfigureAwait(false);
            return (meta, shot.Succeeded && File.Exists(thumb) ? thumb : null);
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
}
