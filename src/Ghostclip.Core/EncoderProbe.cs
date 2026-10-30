namespace Ghostclip.Core;

public sealed record ProbeResult(EncoderKind Encoder, EncoderInput Input, bool Ok, double Seconds, string? Error);

/// <summary>Which encoder/input pairs worked with a given FFmpeg build, monitor and codec.</summary>
public sealed record ProbeCache(string FfmpegId, VideoCodec Codec, int Monitor, DateTime ProbedUtc, IReadOnlyList<ProbeResult> Results)
{
    /// <summary>The first working pair that matches the request, in preference order.</summary>
    public ProbeResult? Choose(EncoderKind encoder, EncoderInput input)
    {
        foreach (var e in EncoderProbe.Candidates(encoder))
        {
            foreach (var i in EncoderProbe.Candidates(input))
            {
                var hit = Results.FirstOrDefault(r => r.Ok && r.Encoder == e && r.Input == i);
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    public bool Matches(string ffmpegId, VideoCodec codec, int monitor) =>
        FfmpegId == ffmpegId && Codec == codec && Monitor == monitor;
}

public static class EncoderProbe
{
    /// <summary>Dedicated GPUs first; QuickSync last, since it may share the GPU with the game.</summary>
    public static IReadOnlyList<EncoderKind> Candidates(EncoderKind requested) =>
        requested == EncoderKind.Auto ? [EncoderKind.Nvenc, EncoderKind.Amf, EncoderKind.Qsv] : [requested];

    /// <summary>
    /// Direct first. Measured on an Intel UHD at 1080p60: lower RAM (144 vs 171 MB)
    /// and CPU, with the color conversion on the dedicated Video Processing engine
    /// instead of 3D. It also avoids scale_d3d11, which fails on that GPU.
    /// </summary>
    public static IReadOnlyList<EncoderInput> Candidates(EncoderInput requested) =>
        requested == EncoderInput.Auto ? [EncoderInput.Direct, EncoderInput.Nv12] : [requested];

    /// <summary>
    /// True when every encoder that was actually tried failed because Windows
    /// refused the desktop (locked screen, UAC prompt, Ctrl+Alt+Del). That says
    /// nothing about the encoders: wait and probe again instead of giving up.
    /// </summary>
    public static bool DesktopUnavailable(IEnumerable<ProbeResult> results)
    {
        var tried = results.Where(r => !r.Ok && r.Error is not null && !r.Error.Contains("is not in this FFmpeg build", StringComparison.Ordinal)).ToList();
        return tried.Count > 0 && !results.Any(r => r.Ok) && tried.All(r => IsDesktopDenied(r.Error));
    }

    /// <summary>FFmpeg's message when Desktop Duplication is refused.</summary>
    public static bool IsDesktopDenied(string? error) =>
        error is not null && error.Contains("duplication access denied", StringComparison.OrdinalIgnoreCase);

    /// <summary>Identifies an FFmpeg binary cheaply, so a replaced binary is probed again.</summary>
    public static string FfmpegId(string path)
    {
        var info = new FileInfo(path);
        return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }
}
