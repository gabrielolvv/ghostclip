using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Only the sound of the trim marks, as a file of its own.
public class ExportAudioTests
{
    private static List<string> Args(int total = 2, IReadOnlyList<int>? keep = null, IReadOnlyList<double>? volumes = null, IReadOnlyList<int>? denoise = null, double start = 10, double end = 25) =>
        [.. FfmpegArgs.AudioOnly("in.mp4", "out.m4a.part", start, end, total, keep, volumes, denoise)];

    private static string Graph(List<string> args) => args[args.IndexOf("-filter_complex") + 1];

    [Fact]
    public void It_takes_the_marks_seeking_before_the_input()
    {
        var args = Args();
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.True(args.IndexOf("-to") < args.IndexOf("-i"));
        Assert.Equal("10.000000", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("25.000", args[args.IndexOf("-to") + 1]);
    }

    [Fact]
    public void The_marks_use_a_point_whatever_the_language_of_Windows()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var args = Args(start: 1.5, end: 2.25);
            Assert.Equal("1.500000", args[args.IndexOf("-ss") + 1]);
            Assert.Equal("2.250", args[args.IndexOf("-to") + 1]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void No_picture_goes_in()
    {
        Assert.All([Args(), Args(keep: [0]), Args(keep: [0], volumes: [0.5, 1])], args =>
        {
            Assert.DoesNotContain("0:v:0", args);
            Assert.DoesNotContain("-c:v", args);
        });
    }

    [Fact]
    public void One_track_as_recorded_is_copied_not_encoded_again()
    {
        var args = Args(keep: [1]);
        Assert.Equal("0:a:1", args[args.IndexOf("-map") + 1]);
        Assert.Equal("copy", args[args.IndexOf("-c:a") + 1]);
        Assert.DoesNotContain("-filter_complex", args);
    }

    [Fact]
    public void A_clip_with_one_track_is_copied_too()
    {
        var args = Args(total: 1);
        Assert.Equal("copy", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void Several_tracks_are_mixed_and_encoded()
    {
        var args = Args();
        Assert.Equal("[0:a:0][0:a:1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args));
        Assert.Equal("[a]", args[args.IndexOf("-map") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void Volumes_and_noise_reduction_apply_as_in_the_copies()
    {
        var args = Args(volumes: [1, 0.5], denoise: [1]);
        Assert.Equal("[0:a:1]afftdn=nf=-30,volume=0.5[v1];[0:a:0][v1]amix=inputs=2:duration=first:normalize=0[a]", Graph(args));
    }

    [Fact]
    public void One_track_with_a_filter_is_encoded()
    {
        var args = Args(keep: [1], denoise: [1]);
        Assert.Equal("[0:a:1]afftdn=nf=-30[a]", Graph(args));
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void It_names_its_format_because_the_output_ends_in_part()
    {
        var args = Args();
        Assert.Equal("mp4", args[args.IndexOf("-f") + 1]);
        Assert.Equal("out.m4a.part", args[^1]);
    }

    [Fact]
    public void It_goes_next_to_its_clip_under_a_name_of_its_own()
    {
        var clip = Path.Combine("G", "G_2026-10-30_21-14-02.mp4");
        var first = Path.Combine("G", "G_2026-10-30_21-14-02_audio.m4a");
        Assert.Equal(first, ClipNaming.DerivedPath(clip, "audio", _ => false, ".m4a"));
        Assert.Equal(Path.Combine("G", "G_2026-10-30_21-14-02_audio_2.m4a"), ClipNaming.DerivedPath(clip, "audio", p => p == first, ".m4a"));
    }

    [Fact]
    public void One_left_half_made_is_cleaned_up_but_a_finished_one_is_not_a_clip()
    {
        var root = Directory.CreateTempSubdirectory("ghostclip-audio-").FullName;
        try
        {
            var game = Directory.CreateDirectory(Path.Combine(root, "G")).FullName;
            var part = Path.Combine(game, "G_2026-10-30_21-14-02_audio.m4a.part");
            File.WriteAllBytes(part, [1]);
            File.SetLastWriteTimeUtc(part, DateTime.UtcNow.AddDays(-2));
            File.WriteAllBytes(Path.Combine(game, "G_2026-10-30_21-14-02_audio.m4a"), [1]);

            Assert.Equal(1, PartFile.CleanAbandoned(root, TimeSpan.FromHours(1), DateTime.UtcNow));
            Assert.False(File.Exists(part));
            Assert.Empty(ClipLibrary.Scan(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
