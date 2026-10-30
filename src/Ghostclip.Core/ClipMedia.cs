using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ghostclip.Core;

/// <summary>What the library shows about a clip, cached next to its thumbnail.</summary>
/// <param name="Keyframes">Seconds where a stream-copy trim can start.</param>
/// <param name="Peaks">Peak level of the first audio track, 0..1, in <see cref="ClipMedia.PeakCount"/> buckets.</param>
public sealed record ClipMeta(double Duration, int Height, double Fps, int AudioTracks, string VideoCodec,
    IReadOnlyList<double> Keyframes, IReadOnlyList<float> Peaks);

/// <summary>Parsing what ffprobe and FFmpeg report about a clip for the library.</summary>
public static class ClipMedia
{
    public const int PeakCount = 400;

    // Peaks are measured on audio resampled to this rate: plenty for a waveform, cheap to scan.
    private const int PeakSampleRate = 8000;

    private const string PeakKey = "lavfi.astats.Overall.Peak_level=";

    /// <summary>From "pts_time,flags" lines (<see cref="FfmpegArgs.Keyframes"/>): the keyframe times, ascending.</summary>
    public static IReadOnlyList<double> ParseKeyframes(string ffprobeCsv)
    {
        var result = new List<double>();
        foreach (var line in ffprobeCsv.Split('\n'))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length >= 2 && parts[1].Contains('K') &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            {
                result.Add(t);
            }
        }
        result.Sort();
        return result;
    }

    /// <summary>Where a stream-copy trim asked to start at <paramref name="seconds"/> really starts.</summary>
    public static double SnapStart(IReadOnlyList<double> keyframes, double seconds)
    {
        var snapped = 0.0;
        foreach (var k in keyframes)
        {
            if (k > seconds) break;
            snapped = k;
        }
        return snapped;
    }

    /// <summary>The peak levels FFmpeg printed (<see cref="FfmpegArgs.AudioPeaks"/>), from dBFS to 0..1.</summary>
    public static IReadOnlyList<float> ParsePeaks(string ffmpegLog)
    {
        var result = new List<float>();
        foreach (var line in ffmpegLog.Split('\n'))
        {
            var at = line.IndexOf(PeakKey, StringComparison.Ordinal);
            if (at < 0) continue;
            var value = line[(at + PeakKey.Length)..].Trim();
            var db = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NegativeInfinity;
            result.Add((float)Math.Clamp(Math.Pow(10, db / 20), 0, 1));
        }
        return result;
    }

    /// <summary>
    /// The meta to cache for a clip, from its probe results. A keyframe list or
    /// waveform that failed or timed out (a long clip probed while a game runs)
    /// throws instead: cached, a partial list would snap trims to the wrong place.
    /// </summary>
    public static ClipMeta BuildMeta(double duration, int height, double fps, int audioTracks, string videoCodec,
        ProcessResult keyframes, ProcessResult? peaks)
    {
        if (!keyframes.Succeeded)
        {
            throw new InvalidOperationException($"Could not list the clip's keyframes: {(keyframes.TimedOut ? "timed out" : keyframes.FirstError)}");
        }
        var keys = ParseKeyframes(keyframes.StdOut);
        if (keys.Count == 0) throw new InvalidOperationException("The clip has no keyframes to trim from.");
        if (audioTracks > 0 && peaks is not { Succeeded: true })
        {
            throw new InvalidOperationException($"Could not read the clip's audio levels: {(peaks is null ? "not measured" : peaks.TimedOut ? "timed out" : peaks.FirstError)}");
        }
        return new ClipMeta(duration, height, Math.Round(fps, 2), audioTracks, videoCodec, keys,
            peaks is null ? [] : ParsePeaks(peaks.StdErr));
    }

    public static int SamplesPerPeak(double durationSeconds) =>
        Math.Max(1, (int)Math.Ceiling(durationSeconds * PeakSampleRate / PeakCount));

    /// <summary>File name stem for a clip's cached thumbnail and meta; a changed file gets a new one.</summary>
    public static string CacheKey(string path, long bytes, DateTime lastWriteUtc)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{Path.GetFullPath(path).ToUpperInvariant()}|{bytes}|{lastWriteUtc.Ticks}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
