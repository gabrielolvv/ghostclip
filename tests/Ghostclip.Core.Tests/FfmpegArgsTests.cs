using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class FfmpegArgsTests
{
    private static CaptureSettings Qsv(EncoderInput input = EncoderInput.Direct, int height = 0) =>
        new(EncoderKind.Qsv, input, VideoCodec.H264, Monitor: 0, Fps: 60, BitrateMbps: 20, OutputHeight: height, QsvAsyncDepth: 1);

    private static string ValueAfter(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

    [Fact]
    public void Gop_is_exactly_one_segment()
    {
        var args = FfmpegArgs.Record(Qsv(), @"C:\buf", 0, [], 0);
        Assert.Equal("120", ValueAfter(args, "-g"));
        Assert.Equal("2", ValueAfter(args, "-segment_time"));
        Assert.Equal("expr:gte(t,n_forced*2)", ValueAfter(args, "-force_key_frames"));
        Assert.Equal("0", ValueAfter(args, "-bf"));
    }

    [Fact]
    public void Captures_at_clip_rate_with_constant_frame_rate()
    {
        var args = FfmpegArgs.Record(Qsv(), @"C:\buf", 0, [], 0);
        Assert.Contains("ddagrab=output_idx=0:framerate=60", ValueAfter(args, "-filter_complex"));
        Assert.Equal("cfr", ValueAfter(args, "-fps_mode"));
    }

    [Fact]
    public void Segments_are_numbered_not_wrapped()
    {
        var args = FfmpegArgs.Record(Qsv(), @"C:\buf", 1234, [], 0);
        Assert.DoesNotContain("-segment_wrap", args);
        Assert.Equal("1234", ValueAfter(args, "-segment_start_number"));
        Assert.Equal(@"C:\buf\seg_%08d.ts", args[^1]);
        Assert.Equal("mpegts", ValueAfter(args, "-segment_format"));
    }

    [Fact]
    public void Qsv_uses_low_power_and_hwmap()
    {
        var args = FfmpegArgs.Record(Qsv(), @"C:\b", 0, [], 0);
        Assert.Equal("h264_qsv", ValueAfter(args, "-c:v"));
        Assert.Equal("1", ValueAfter(args, "-low_power"));
        Assert.Equal("1", ValueAfter(args, "-async_depth"));
        Assert.EndsWith("hwmap=derive_device=qsv,format=qsv[v]", ValueAfter(args, "-filter_complex"));
    }

    [Fact]
    public void Nv12_and_scaling_add_one_gpu_conversion_step()
    {
        Assert.EndsWith(",vpp_qsv=format=nv12", FfmpegArgs.Video(Qsv(EncoderInput.Nv12)).Filter);
        Assert.EndsWith(",vpp_qsv=format=nv12:w=-1:h=720", FfmpegArgs.Video(Qsv(EncoderInput.Direct, 720)).Filter);

        var nvenc = Qsv(EncoderInput.Nv12) with { Encoder = EncoderKind.Nvenc };
        Assert.EndsWith(",scale_d3d11=format=nv12", FfmpegArgs.Video(nvenc).Filter);
        Assert.DoesNotContain("hwmap", FfmpegArgs.Video(nvenc with { Input = EncoderInput.Direct }).Filter);
    }

    // A Radeon RX 6600's AMF refused ddagrab's texture (SubmitInput error 18) and scale_d3d11's NV12 one
    // couldn't even be created, while BGRA from system memory encoded fine.
    [Fact]
    public void Memory_input_copies_frames_down_and_keeps_bgra()
    {
        var amf = Qsv(EncoderInput.Memory) with { Encoder = EncoderKind.Amf };
        Assert.Equal("ddagrab=output_idx=0:framerate=60,hwdownload,format=bgra", FfmpegArgs.Video(amf).Filter);
        Assert.Equal("ddagrab=output_idx=0:framerate=60,hwdownload,format=bgra,scale=-2:720", FfmpegArgs.Video(amf with { OutputHeight = 720 }).Filter);
        Assert.Equal("h264_amf", ValueAfter(FfmpegArgs.Probe(amf, 30), "-c:v"));
        Assert.Equal("h264_amf (memory)", amf.Describe());
    }

    [Theory]
    [InlineData(EncoderKind.Nvenc, VideoCodec.H264, "h264_nvenc")]
    [InlineData(EncoderKind.Amf, VideoCodec.Hevc, "hevc_amf")]
    [InlineData(EncoderKind.Qsv, VideoCodec.Hevc, "hevc_qsv")]
    public void Picks_the_hardware_encoder(EncoderKind encoder, VideoCodec codec, string expected)
    {
        var s = Qsv() with { Encoder = encoder, Codec = codec };
        Assert.Equal(expected, s.EncoderName);
        Assert.Equal(expected, ValueAfter(FfmpegArgs.Probe(s, 30), "-c:v"));
    }

    [Fact]
    public void Never_builds_a_cpu_encoder_command()
    {
        Assert.Throws<ArgumentException>(() => FfmpegArgs.Video(Qsv() with { Encoder = EncoderKind.Auto }));
        Assert.Throws<ArgumentException>(() => FfmpegArgs.Video(Qsv(EncoderInput.Auto)));
    }

    [Fact]
    public void Audio_comes_from_the_pipe_as_raw_float()
    {
        var args = FfmpegArgs.Record(Qsv(), @"C:\b", 0, [@"\\.\pipe\gc-audio"], 120);
        Assert.Equal("f32le", ValueAfter(args, "-f"));
        Assert.Equal("48000", ValueAfter(args, "-ar"));
        Assert.Equal(@"\\.\pipe\gc-audio", ValueAfter(args, "-i"));
        Assert.Equal("0.120", ValueAfter(args, "-itsoffset"));
        Assert.Equal("0:a", args[args.ToList().LastIndexOf("-map") + 1]);
    }

    [Fact]
    public void No_audio_means_video_only()
    {
        var args = FfmpegArgs.Record(Qsv(), @"C:\b", 0, [], 0);
        Assert.DoesNotContain("-i", args);
        Assert.DoesNotContain("0:a", args);
    }

    [Fact]
    public void Numbers_ignore_the_current_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            var args = FfmpegArgs.Record(Qsv(), @"C:\b", 0, [@"\\.\pipe\a"], -50);
            Assert.Equal("0.00833", ValueAfter(args, "-segment_time_delta"));
            Assert.Equal("-0.050", ValueAfter(args, "-itsoffset"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Remux_is_stream_copy_through_the_concat_demuxer()
    {
        var args = FfmpegArgs.Remux(@"C:\p\list.ffconcat", @"C:\out.mp4");
        Assert.Equal("concat", ValueAfter(args, "-f"));
        Assert.Equal("copy", ValueAfter(args, "-c"));
        Assert.Equal(@"C:\out.mp4", args[^1]);
    }

    [Fact]
    public void Concat_list_escapes_quotes_and_uses_forward_slashes()
    {
        var list = FfmpegArgs.ConcatList([@"C:\a b\seg_1.ts", "it's.ts"]);
        Assert.Equal("ffconcat version 1.0\nfile 'C:/a b/seg_1.ts'\nfile 'it'\\''s.ts'\n", list);
    }
}
