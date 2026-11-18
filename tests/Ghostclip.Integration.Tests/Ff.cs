using System.Globalization;
using System.Text.RegularExpressions;
using Ghostclip.Core;

namespace Ghostclip.Integration.Tests;

/// <summary>
/// The FFmpeg the tests run, from GHOSTCLIP_FFMPEG_DIR: in CI, the minimal LGPL build the app ships
/// (build/ffmpeg), so these also show that build can do what the library asks of it. Without it, the tests are
/// skipped. Only what needs no GPU encoder runs here: stream copies, the sound, GIFs and pictures.
/// </summary>
internal static partial class Ff
{
    public static string? Dir => Environment.GetEnvironmentVariable("GHOSTCLIP_FFMPEG_DIR") is { Length: > 0 } dir ? dir : null;

    /// <summary>6 s at 30 fps, a keyframe every second and no B-frames, as Ghostclip records; tracks "Game" (440 Hz) and "Microphone" (880 Hz).</summary>
    public static string Clip => Path.Combine(AppContext.BaseDirectory, "Fixtures", "two-tracks.mp4");

    public static readonly double[] ClipKeyframes = [0, 1, 2, 3, 4, 5];

    public const double ClipSeconds = 6;

    public static void SkipWithout() => Assert.SkipWhen(Dir is null, "GHOSTCLIP_FFMPEG_DIR isn't set");

    public static string Temp() => Directory.CreateTempSubdirectory("ghostclip-it-").FullName;

    public static async Task RunAsync(IReadOnlyList<string> args)
    {
        var r = await ProcessRunner.RunAsync(Path.Combine(Dir!, "ffmpeg.exe"), args, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        Assert.True(r.Succeeded, $"ffmpeg failed: {r.FirstError}");
    }

    /// <summary>ffmpeg or ffprobe with these arguments, as the app runs them; the whole result, which must have succeeded.</summary>
    public static async Task<ProcessResult> ToolAsync(string tool, IReadOnlyList<string> args)
    {
        var r = await ProcessRunner.RunAsync(Path.Combine(Dir!, tool + ".exe"), args, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        Assert.True(r.Succeeded, $"{tool} failed: {r.FirstError}");
        return r;
    }

    private static async Task<string> ProbeAsync(params string[] args)
    {
        var r = await ProcessRunner.RunAsync(Path.Combine(Dir!, "ffprobe.exe"), ["-v", "error", .. args], TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        Assert.True(r.Succeeded, $"ffprobe failed: {r.FirstError}");
        return r.StdOut;
    }

    /// <summary>The MD5 of each packet of a stream, in file order: a stream copy keeps them as they were.</summary>
    public static async Task<IReadOnlyList<string>> PacketHashesAsync(string file, string stream) =>
        [.. (await ProbeAsync("-select_streams", stream, "-show_data_hash", "md5", "-show_entries", "packet=data_hash", "-of", "csv=p=0", file).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>The presentation time of each video packet, in file order.</summary>
    public static async Task<IReadOnlyList<double>> VideoTimesAsync(string file) =>
        [.. (await ProbeAsync("-select_streams", "v:0", "-show_entries", "packet=pts_time", "-of", "csv=p=0", file).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => double.Parse(t, CultureInfo.InvariantCulture))];

    public static async Task<double> DurationAsync(string file) =>
        double.Parse((await ProbeAsync("-show_entries", "format=duration", "-of", "csv=p=0", file).ConfigureAwait(false)).Trim(), CultureInfo.InvariantCulture);

    /// <summary>Each stream's start time, and the name of each audio track, in stream order.</summary>
    public static async Task<IReadOnlyList<(string Type, double Start, string Name)>> StreamInfoAsync(string file) =>
        [.. (await ProbeAsync("-show_entries", "stream=codec_type,start_time:stream_tags=handler_name", "-of", "csv=p=0", file).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(','))
            .Select(p => (p[0], double.Parse(p[1], CultureInfo.InvariantCulture), p.Length > 2 ? p[2] : ""))];

    /// <summary>
    /// Cuts the clip into 2 s MPEG-TS segments named as the recorder names them, with the recorder's segment options:
    /// what the replay buffer holds.
    /// </summary>
    public static async Task<IReadOnlyList<string>> SegmentsAsync(string dir, long startNumber)
    {
        Directory.CreateDirectory(dir);
        await RunAsync([
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", Clip, "-map", "0", "-c", "copy",
            "-f", "segment", "-segment_time", "2", "-segment_format", "mpegts",
            "-segment_start_number", startNumber.ToString(CultureInfo.InvariantCulture), "-reset_timestamps", "0",
            Path.Combine(dir, SegmentRing.FfmpegPattern),
        ]).ConfigureAwait(false);
        return [.. Directory.GetFiles(dir, "seg_*.ts").Order(StringComparer.Ordinal)];
    }

    public static async Task<IReadOnlyList<string>> StreamsAsync(string file) =>
        [.. (await ProbeAsync("-show_entries", "stream=codec_type", "-of", "csv=p=0", file).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>The loudest a track gets between two times, in dB; about -91 for silence.</summary>
    public static async Task<double> PeakAsync(string file, int track, double from, double to)
    {
        var r = await ProcessRunner.RunAsync(Path.Combine(Dir!, "ffmpeg.exe"),
            ["-hide_banner", "-nostdin", "-ss", Inv(from), "-to", Inv(to), "-i", file, "-map", $"0:a:{track}", "-af", "volumedetect", "-f", "null", "-"],
            TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        Assert.True(r.Succeeded, $"volumedetect failed: {r.FirstError}");
        var m = MaxVolume().Match(r.StdErr);
        Assert.True(m.Success, "no max_volume in the output");
        return m.Groups[1].Value == "-inf" ? double.NegativeInfinity : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string Inv(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"max_volume: (-?[\d.]+|-inf) dB")]
    private static partial Regex MaxVolume();
}
