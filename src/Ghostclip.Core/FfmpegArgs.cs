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
    /// Each audio track (game or system audio, microphone) arrives as raw float
    /// PCM on its own named pipe and becomes its own AAC track.
    /// </summary>
    public static IReadOnlyList<string> Record(CaptureSettings s, string bufferDir, long startNumber, IReadOnlyList<string> audioPipes, int audioOffsetMs)
    {
        var (init, filter, codec) = Video(s);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1", "-stats_period", "1",
        };
        args.AddRange(init);
        foreach (var pipe in audioPipes)
        {
            args.AddRange(["-thread_queue_size", "512"]);
            if (audioOffsetMs != 0) args.AddRange(["-itsoffset", Inv($"{audioOffsetMs / 1000.0:0.000}")]);
            args.AddRange([
                "-f", "f32le", "-ar", Inv($"{AudioSampleRate}"), "-ac", Inv($"{AudioChannels}"), "-i", pipe,
            ]);
        }
        args.AddRange(["-filter_complex", filter + "[v]", "-map", "[v]"]);
        for (var i = 0; i < audioPipes.Count; i++)
        {
            args.AddRange(["-map", Inv($"{i}:a")]);
        }
        if (audioPipes.Count > 0)
        {
            args.AddRange(["-c:a", "aac", "-b:a", "160k"]);
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
    public static IReadOnlyList<string> Remux(string concatListPath, string outputPath, int audioLeadMs = 0, IReadOnlyList<string>? audioTitles = null)
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
        args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero"]);
        // Names the tracks, so a player shows "Game" and "Microphone" to pick from.
        // MP4 keeps the name in handler_name; title is for players that read it elsewhere.
        for (var i = 0; i < (audioTitles?.Count ?? 0); i++)
        {
            var key = Inv($"-metadata:s:a:{i}");
            args.AddRange([key, $"handler_name={audioTitles![i]}", key, $"title={audioTitles[i]}"]);
        }
        args.AddRange(["-f", "mp4", outputPath]);
        return args;
    }

    /// <summary>
    /// Cuts by stream copy: no re-encode, done in about the time it takes to
    /// copy the file. The cut starts at the keyframe at or before
    /// <paramref name="startSeconds"/>, so with 2 s GOPs it can start up to 2 s early.
    /// </summary>
    public static IReadOnlyList<string> Trim(string input, string output, double startSeconds, double endSeconds) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        // The start is a keyframe time from the library (8.233333 at 60 fps): rounded to
        // milliseconds it would fall just before the keyframe and cut from the one before.
        "-ss", Inv($"{startSeconds:0.000000}"), "-to", Inv($"{endSeconds:0.000}"), "-i", input,
        "-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero", "-movflags", "+faststart",
        "-f", "mp4", output,
    ];

    /// <summary>One frame as a small JPEG for the library. Seeking before the input decodes from the nearest keyframe only.</summary>
    public static IReadOnlyList<string> Thumbnail(string input, string output, double atSeconds) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
        "-ss", Inv($"{atSeconds:0.000}"), "-i", input,
        "-frames:v", "1", "-vf", "scale=480:-2", "-q:v", "4", "-f", "image2", output,
    ];

    /// <summary>
    /// Prints the peak level of each bucket of the first audio track
    /// (<see cref="ClipMedia.ParsePeaks"/>). Audio only, resampled down: no video is decoded.
    /// </summary>
    public static IReadOnlyList<string> AudioPeaks(string input, int samplesPerPeak) =>
    [
        "-hide_banner", "-nostdin", "-loglevel", "info", "-i", input, "-map", "0:a:0",
        "-af", Inv($"aresample=8000,asetnsamples=n={samplesPerPeak}:p=0,astats=metadata=1:reset=1,ametadata=mode=print:key=lavfi.astats.Overall.Peak_level"),
        "-f", "null", "-",
    ];

    /// <summary>ffprobe arguments listing video packets and their flags; "K" marks keyframes. Reads the index, decodes nothing.</summary>
    public static IReadOnlyList<string> Keyframes(string input) =>
    [
        "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time,flags", "-of", "csv=p=0", input,
    ];

    /// <summary>
    /// Re-encodes a clip to fit a size limit, on the hardware encoder. Several
    /// audio tracks are mixed into one, since Discord plays only the first.
    /// </summary>
    public static IReadOnlyList<string> Compress(string input, string output, CompressionPlan plan, string encoderName, int audioTracks)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-hwaccel", "d3d11va", "-i", input };
        if (audioTracks > 1)
        {
            var inputs = string.Concat(Enumerable.Range(0, audioTracks).Select(i => Inv($"[0:a:{i}]")));
            args.AddRange(["-filter_complex", Inv($"{inputs}amix=inputs={audioTracks}:duration=first:normalize=0[a]"), "-map", "0:v:0", "-map", "[a]"]);
        }
        else
        {
            args.AddRange(["-map", "0:v:0", "-map", "0:a:0?"]);
        }

        var vf = new List<string>();
        if (plan.Height > 0) vf.Add(Inv($"scale=-2:{plan.Height}"));
        if (plan.Fps > 0) vf.Add(Inv($"fps={plan.Fps}"));
        vf.Add("format=nv12");
        var rate = Inv($"{plan.VideoKbps}k");
        var gop = Inv($"{(plan.Fps > 0 ? plan.Fps : 60) * 2}");
        args.AddRange([
            "-vf", string.Join(',', vf),
            "-c:v", encoderName, "-b:v", rate, "-maxrate", rate, "-bufsize", Inv($"{plan.VideoKbps * 2}k"), "-g", gop,
            "-c:a", "aac", "-b:a", Inv($"{plan.AudioKbps}k"),
            "-movflags", "+faststart", "-f", "mp4", output,
        ]);
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
