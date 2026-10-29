using System.Globalization;
using System.Text;

namespace Ghostclip.Core;

/// <summary>Resolved capture parameters: no Auto values left.</summary>
public sealed record CaptureSettings(
    EncoderKind Encoder,
    EncoderInput Input,
    VideoCodec Codec,
    int Monitor,
    int Fps,
    int BitrateMbps,
    int OutputHeight,
    int QsvAsyncDepth,
    bool LowPower = true)
{
    public static CaptureSettings From(GhostclipConfig config, EncoderKind encoder, EncoderInput input) => new(
        encoder, input, config.Codec, config.Monitor, config.Fps, config.BitrateMbps,
        config.OutputHeight, config.QsvAsyncDepth);

    public string EncoderName =>
        $"{(Codec == VideoCodec.H264 ? "h264" : "hevc")}_{Encoder.ToString().ToLowerInvariant()}";

    public string Describe() => $"{EncoderName} ({Input.ToString().ToLowerInvariant()})";
}

/// <summary>Builds FFmpeg command lines. Kept pure so the exact flags are unit tested.</summary>
public static class FfmpegArgs
{
    public const int AudioSampleRate = 48000;
    public const int AudioChannels = 2;
    public const int AudioBytesPerFrame = AudioChannels * sizeof(float);

    /// <summary>Hardware device setup, capture filter and encoder flags.</summary>
    public static (string[] Init, string Filter, string[] Codec) Video(CaptureSettings s)
    {
        if (s.Encoder == EncoderKind.Auto || s.Input == EncoderInput.Auto)
        {
            throw new ArgumentException("Encoder and input must be resolved before building arguments.", nameof(s));
        }

        // ddagrab captures at the clip rate, never at the monitor's refresh rate.
        var filter = new StringBuilder(Inv($"ddagrab=output_idx={s.Monitor}:framerate={s.Fps}"));
        var nv12 = s.Input == EncoderInput.Nv12 || s.OutputHeight > 0;
        if (s.Encoder == EncoderKind.Qsv)
        {
            filter.Append(",hwmap=derive_device=qsv,format=qsv");
            if (nv12)
            {
                filter.Append(",vpp_qsv=format=nv12");
                if (s.OutputHeight > 0) filter.Append(Inv($":w=-1:h={s.OutputHeight}"));
            }
        }
        else if (nv12)
        {
            filter.Append(",scale_d3d11=format=nv12");
            if (s.OutputHeight > 0) filter.Append(Inv($":width=-2:height={s.OutputHeight}"));
        }

        var gop = s.Fps * GhostclipConfig.SegmentSeconds;
        var rate = Inv($"{s.BitrateMbps}M");
        // Constant bitrate keeps the disk buffer size predictable. A fixed GOP of
        // exactly one segment, no B-frames and forced IDRs make every segment
        // start on a keyframe that decodes on its own.
        var codec = new List<string>
        {
            "-r", Inv($"{s.Fps}"), "-fps_mode", "cfr", "-g", Inv($"{gop}"), "-bf", "0",
            "-b:v", rate, "-maxrate", rate, "-bufsize", rate,
            "-c:v", s.EncoderName,
        };
        var forceKey = new[] { "-force_key_frames", Inv($"expr:gte(t,n_forced*{GhostclipConfig.SegmentSeconds})") };
        switch (s.Encoder)
        {
            case EncoderKind.Nvenc:
                // p1 + ll: the lightest load on the NVENC block.
                codec.AddRange(["-preset", "p1", "-tune", "ll", "-rc", "cbr", "-no-scenecut", "1", "-forced-idr", "1"]);
                codec.AddRange(forceKey);
                break;
            case EncoderKind.Amf:
                codec.AddRange(["-usage", "lowlatency", "-quality", "speed", "-rc", "cbr", "-header_insertion_mode", "idr"]);
                break;
            case EncoderKind.Qsv:
                codec.AddRange(["-preset", "veryfast", "-forced_idr", "1", "-async_depth", Inv($"{s.QsvAsyncDepth}")]);
                if (s.Codec == VideoCodec.H264) codec.AddRange(["-look_ahead", "0"]);
                // low_power uses the fixed-function encoder (VDENC) instead of the
                // GPU's execution units, which an iGPU shares with the game.
                if (s.LowPower) codec.AddRange(["-low_power", "1"]);
                codec.AddRange(forceKey);
                break;
        }

        return (["-init_hw_device", "d3d11va=gc", "-filter_hw_device", "gc"], filter.ToString(), codec.ToArray());
    }

    /// <summary>Encodes a few captured frames into the null muxer. Nothing touches the disk.</summary>
    public static IReadOnlyList<string> Probe(CaptureSettings s, int frames)
    {
        var (init, filter, codec) = Video(s);
        return [
            "-hide_banner", "-nostdin", "-loglevel", "error", .. init,
            "-filter_complex", filter, "-frames:v", Inv($"{frames}"), .. codec,
            "-f", "null", "-",
        ];
    }

    /// <summary>
    /// The recorder: capture, encode, and 2 s MPEG-TS segments numbered from
    /// <paramref name="startNumber"/>. Progress goes to stdout as key=value lines.
    /// System audio, when present, arrives as raw float PCM on a named pipe.
    /// </summary>
    public static IReadOnlyList<string> Record(CaptureSettings s, string bufferDir, long startNumber, string? audioPipe, int audioOffsetMs)
    {
        var (init, filter, codec) = Video(s);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1", "-stats_period", "1",
        };
        args.AddRange(init);
        if (audioPipe is not null)
        {
            args.AddRange(["-thread_queue_size", "512"]);
            if (audioOffsetMs != 0) args.AddRange(["-itsoffset", Inv($"{audioOffsetMs / 1000.0:0.000}")]);
            args.AddRange([
                "-f", "f32le", "-ar", Inv($"{AudioSampleRate}"), "-ac", Inv($"{AudioChannels}"), "-i", audioPipe,
            ]);
        }
        args.AddRange(["-filter_complex", filter + "[v]", "-map", "[v]"]);
        if (audioPipe is not null)
        {
            args.AddRange(["-map", "0:a", "-c:a", "aac", "-b:a", "160k"]);
        }
        args.AddRange(codec);
        // Half a frame of slack lets the cut land exactly on the forced keyframe.
        var delta = 1.0 / (2 * s.Fps);
        args.AddRange([
            "-f", "segment",
            "-segment_time", Inv($"{GhostclipConfig.SegmentSeconds}"),
            "-segment_time_delta", Inv($"{delta:0.00000}"),
            "-segment_format", "mpegts",
            "-segment_start_number", Inv($"{startNumber}"),
            "-reset_timestamps", "0",
            Path.Combine(bufferDir, SegmentRing.FfmpegPattern),
        ]);
        return args;
    }

    /// <summary>
    /// Joins a snapshot into an MP4 by stream copy. The concat demuxer (rather
    /// than the concat protocol) rebases each file's timestamps, so a clip that
    /// spans a worker restart still has monotonic timestamps. With an audio lead
    /// the list is read twice, video from one and audio shifted from the other
    /// (see <see cref="AudioSync"/>).
    /// </summary>
    public static IReadOnlyList<string> Remux(string concatListPath, string outputPath, int audioLeadMs = 0)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" };
        if (audioLeadMs == 0)
        {
            args.AddRange(["-f", "concat", "-safe", "0", "-i", concatListPath, "-map", "0"]);
        }
        else
        {
            args.AddRange([
                "-f", "concat", "-safe", "0", "-i", concatListPath,
                "-itsoffset", Inv($"{-audioLeadMs / 1000.0:0.000}"),
                "-f", "concat", "-safe", "0", "-i", concatListPath,
                "-map", "0:v", "-map", "1:a?",
            ]);
        }
        args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero", "-f", "mp4", outputPath]);
        return args;
    }

    /// <summary>An ffconcat list. Paths use forward slashes and single quotes are escaped.</summary>
    public static string ConcatList(IEnumerable<string> files)
    {
        var sb = new StringBuilder("ffconcat version 1.0\n");
        foreach (var file in files)
        {
            var path = file.Replace('\\', '/').Replace("'", @"'\''", StringComparison.Ordinal);
            sb.Append("file '").Append(path).Append("'\n");
        }
        return sb.ToString();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
