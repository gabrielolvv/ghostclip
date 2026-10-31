using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ghostclip.Core;

/// <summary>What the library shows about a clip, cached next to its thumbnail.</summary>
/// <param name="Keyframes">Seconds where a stream-copy trim can start.</param>
/// <param name="Peaks">Peak level of the first audio track, 0..1, in <see cref="ClipMedia.PeakCount"/> buckets.</param>
/// <param name="AudioTitles">The tracks' names as saving wrote them ("Game", "Microphone", "Discord"); null in details cached before they were read.</param>
public sealed record ClipMeta(double Duration, int Height, double Fps, int AudioTracks, string VideoCodec,
    IReadOnlyList<double> Keyframes, IReadOnlyList<float> Peaks, IReadOnlyList<string>? AudioTitles = null);

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
        ProcessResult keyframes, ProcessResult? peaks, IReadOnlyList<string>? audioTitles = null)
    {
        if (!keyframes.Succeeded)
        {
            throw new InvalidOperationException(UiText.L($"Could not list the clip's keyframes: {(keyframes.TimedOut ? "timed out" : keyframes.FirstError)}",
                $"Não foi possível listar os keyframes do clipe: {(keyframes.TimedOut ? "tempo esgotado" : keyframes.FirstError)}"));
        }
        var keys = ParseKeyframes(keyframes.StdOut);
        if (keys.Count == 0) throw new InvalidOperationException(UiText.L("The clip has no keyframes to trim from.", "O clipe não tem keyframes para cortar."));
        if (audioTracks > 0 && peaks is not { Succeeded: true })
        {
            throw new InvalidOperationException(UiText.L(
                $"Could not read the clip's audio levels: {(peaks is null ? "not measured" : peaks.TimedOut ? "timed out" : peaks.FirstError)}",
                $"Não foi possível ler os níveis de áudio do clipe: {(peaks is null ? "não medido" : peaks.TimedOut ? "tempo esgotado" : peaks.FirstError)}"));
        }
        return new ClipMeta(duration, height, Math.Round(fps, 2), audioTracks, videoCodec, keys,
            peaks is null ? [] : ParsePeaks(peaks.StdErr), audioTitles);
    }

    /// <summary>
    /// Deletes cached thumbnails and details (16-hex-digit names) not used for <paramref name="age"/>,
    /// such as those of clips deleted long ago. Returns how many files went.
    /// </summary>
    public static int PruneCache(string dir, TimeSpan age, DateTime nowUtc)
    {
        if (!Directory.Exists(dir)) return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            // "<key>.jpg", "<key>.json", or what a cut-off write left: "<key>.jpg.<guid>.part", "<key>.json.<pid>.<tid>.tmp".
            var fileName = Path.GetFileName(file);
            var key = fileName.Split('.')[0];
            if (key.Length != 16 || !key.All(Uri.IsHexDigit)) continue;
            var leftover = fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (nowUtc - File.GetLastWriteTimeUtc(file) < (leftover ? TimeSpan.FromDays(1) : age)) continue;
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    /// <summary>Marks a cache file as used now, so <see cref="PruneCache"/> keeps it.</summary>
    public static void Touch(string file, DateTime nowUtc)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, nowUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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
