using System.Text.Json;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Worker;

/// <summary>
/// Finds a working hardware encoder by test-encoding a few captured frames.
/// The result is cached per FFmpeg binary, codec and monitor, so a worker
/// restart doesn't pay for the probe again.
/// </summary>
internal sealed class EncoderProber(GhostclipPaths paths, ILogger log)
{
    private const int ProbeFrames = 30;

    public async Task<ProbeResult?> ChooseAsync(string ffmpeg, GhostclipConfig config, bool force, CancellationToken ct)
    {
        var id = EncoderProbe.FfmpegId(ffmpeg);
        if (!force && Load() is { } cached && cached.Matches(id, config.Codec, config.Monitor) &&
            cached.Choose(config.Encoder, config.Input) is { } hit)
        {
            return hit;
        }

        log.Information("Probing hardware encoders with {Ffmpeg}", ffmpeg);
        var list = await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-encoders"], TimeSpan.FromSeconds(15), ct: ct).ConfigureAwait(false);
        var results = new List<ProbeResult>();
        foreach (var (encoder, input) in EncoderProbe.Pairs(config.Encoder, config.Input))
        {
            // The pairs through memory come last and are only tried when none on the GPU works.
            if (input == EncoderInput.Memory && results.Any(r => r.Ok)) break;
            var settings = CaptureSettings.From(config, encoder, input);
            if (!list.StdOut.Contains($" {settings.EncoderName} ", StringComparison.Ordinal))
            {
                results.Add(new ProbeResult(encoder, input, false, 0, $"{settings.EncoderName} is not in this FFmpeg build"));
                continue;
            }
            var r = await ProcessRunner.RunAsync(ffmpeg, FfmpegArgs.Probe(settings, ProbeFrames), TimeSpan.FromSeconds(20), ct: ct).ConfigureAwait(false);
            var result = new ProbeResult(encoder, input, r.Succeeded, Math.Round(r.Elapsed.TotalSeconds, 2), r.Succeeded ? null : r.FirstError);
            log.Information("Probe {Encoder} ({Input}): {Outcome}", settings.EncoderName, input, result.Ok ? "ok" : result.Error);
            results.Add(result);
        }

        if (EncoderProbe.DesktopUnavailable(results))
        {
            // A locked screen: nothing was learned about the encoders, so nothing is cached.
            throw new DesktopUnavailableException();
        }
        var cache = new ProbeCache(id, config.Codec, config.Monitor, DateTime.UtcNow, results);
        Save(cache);
        return cache.Choose(config.Encoder, config.Input);
    }

    private ProbeCache? Load()
    {
        try
        {
            if (!File.Exists(paths.ProbeFile)) return null;
            using var stream = File.OpenRead(paths.ProbeFile);
            return JsonSerializer.Deserialize(stream, CoreJson.Default.ProbeCache);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            log.Warning("Ignoring unreadable probe cache: {Error}", ex.Message);
            return null;
        }
    }

    private void Save(ProbeCache cache)
    {
        try
        {
            using var stream = File.Create(paths.ProbeFile);
            JsonSerializer.Serialize(stream, cache, CoreJson.Indented.ProbeCache);
        }
        catch (IOException ex)
        {
            log.Warning("Could not save the probe cache: {Error}", ex.Message);
        }
    }
}

/// <summary>Windows is not letting anyone capture the desktop right now (locked screen, UAC prompt).</summary>
internal sealed class DesktopUnavailableException() : Exception("The desktop can't be captured right now (locked or secure screen).");
