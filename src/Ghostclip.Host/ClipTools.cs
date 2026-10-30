using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

internal sealed record ClipInfo(double Duration, int Height, double Fps, int AudioTracks, string VideoCodec);

/// <summary>
/// Trimming and "compress for Discord". Both run at below-normal priority, so a
/// game in the foreground keeps the CPU.
/// </summary>
internal sealed class ClipTools(GhostclipPaths paths, Func<GhostclipConfig> config, ILogger log)
{
    public async Task<ClipInfo> ProbeAsync(string clip)
    {
        var r = await ProcessRunner.RunAsync(Ffprobe(), [
            "-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,height,r_frame_rate", "-of", "json", clip,
        ], TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        if (!r.Succeeded) throw new InvalidOperationException($"Could not read the clip: {r.FirstError}");

        using var json = JsonDocument.Parse(r.StdOut);
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        int height = 0, audio = 0;
        double fps = 0;
        var codec = "";
        foreach (var s in json.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            if (type == "audio") audio++;
            if (type == "video" && height == 0)
            {
                height = s.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
                var rate = s.TryGetProperty("r_frame_rate", out var f) ? f.GetString() ?? "" : "";
                var parts = rate.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var n) &&
                    double.TryParse(parts[1], CultureInfo.InvariantCulture, out var d) && d > 0)
                {
                    fps = n / d;
                }
            }
        }
        return new ClipInfo(duration, height, fps, audio, codec);
    }

    /// <summary>Cuts by stream copy into Game_..._trim.mp4. Takes about as long as copying the file.</summary>
    public async Task<string> TrimAsync(string clip, double start, double end)
    {
        var output = ClipNaming.DerivedPath(clip, "trim", File.Exists);
        var r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Trim(clip, output, start, end),
            TimeSpan.FromMinutes(2), ProcessPriorityClass.BelowNormal).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            TryDelete(output);
            throw new InvalidOperationException($"Could not trim the clip: {r.FirstError}");
        }
        log.Information("Trimmed {Clip} to {Start:0.0}-{End:0.0} s: {Output}", clip, start, end, output);
        return output;
    }

    /// <summary>
    /// Re-encodes into Game_..._discord.mp4 under the size limit, on the hardware
    /// encoder, always as H.264 (Discord doesn't embed HEVC). Retries once,
    /// lower, if the encoder overshoots.
    /// </summary>
    public async Task<(string Path, long Bytes)> CompressAsync(string clip)
    {
        var cfg = config();
        var info = await ProbeAsync(clip).ConfigureAwait(false);
        var limit = cfg.DiscordLimitMB;
        var plan = CompressionPlanner.Plan(info.Duration, info.Height, info.Fps, limit);
        if (plan is null)
        {
            var max = limit * 8.0 * 1024 * 0.92 / (CompressionPlanner.MinVideoKbps + CompressionPlanner.AudioKbps);
            throw new InvalidOperationException($"{info.Duration:0} s is too long for {limit} MB. Trim it to {max:0} s or less first.");
        }

        var encoder = HardwareEncoder(cfg);
        var output = ClipNaming.DerivedPath(clip, "discord", File.Exists);
        var size = await EncodeAsync(clip, output, plan, encoder, info.AudioTracks).ConfigureAwait(false);
        var limitBytes = limit * 1024L * 1024;
        if (size > limitBytes)
        {
            var retry = CompressionPlanner.Plan(info.Duration, info.Height, info.Fps, limit, CompressionPlanner.RetrySafety(size, limit));
            if (retry is not null)
            {
                log.Information("Compressed clip came out at {Size} bytes, over {Limit} MB; retrying at {Kbps} kbps", size, limit, retry.VideoKbps);
                size = await EncodeAsync(clip, output, retry, encoder, info.AudioTracks).ConfigureAwait(false);
            }
        }
        log.Information("Compressed {Clip} to {Output} ({Size} bytes, {Kbps} kbps, {Height}p)", clip, output, size, plan.VideoKbps, plan.Height);
        return (output, size);
    }

    private async Task<long> EncodeAsync(string clip, string output, CompressionPlan plan, string encoder, int audioTracks)
    {
        var r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Compress(clip, output, plan, encoder, audioTracks),
            TimeSpan.FromMinutes(15), ProcessPriorityClass.BelowNormal).ConfigureAwait(false);
        if (!r.Succeeded)
        {
            TryDelete(output);
            throw new InvalidOperationException($"Could not compress the clip: {r.FirstError}");
        }
        return new FileInfo(output).Length;
    }

    // The encoder the recorder probed as working. Never a CPU encoder.
    private string HardwareEncoder(GhostclipConfig cfg)
    {
        ProbeCache? cache = null;
        if (File.Exists(paths.ProbeFile))
        {
            using var stream = File.OpenRead(paths.ProbeFile);
            cache = JsonSerializer.Deserialize(stream, CoreJson.Default.ProbeCache);
        }
        var choice = cache?.Choose(cfg.Encoder, EncoderInput.Auto)
            ?? throw new InvalidOperationException("No hardware encoder has been detected yet. Let Ghostclip record for a moment first.");
        return CaptureSettings.From(cfg with { Codec = VideoCodec.H264 }, choice.Encoder, choice.Input).EncoderName;
    }

    internal string Ffmpeg() => FfmpegLocator.Find(config().FfmpegPath, AppContext.BaseDirectory)
        ?? throw new InvalidOperationException("FFmpeg was not found.");

    internal string Ffprobe()
    {
        var probe = Path.Combine(Path.GetDirectoryName(Ffmpeg())!, "ffprobe.exe");
        return File.Exists(probe) ? probe : throw new InvalidOperationException("ffprobe.exe was not found next to FFmpeg.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
