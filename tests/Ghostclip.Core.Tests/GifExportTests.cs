using Ghostclip.Core;
using Xunit;

namespace Ghostclip.Core.Tests;

public class GifExportTests
{
    private static IReadOnlyList<string> Args(double start = 10, double length = 15) =>
        FfmpegArgs.Gif("in.mp4", "out.gif.part", start, length);

    private static string Filter(IReadOnlyList<string> args) => args[args.ToList().IndexOf("-vf") + 1];

    [Fact]
    public void Gif_seeks_before_the_input_so_a_long_session_is_not_decoded_from_its_start()
    {
        var args = Args().ToList();
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.True(args.IndexOf("-t") < args.IndexOf("-i"));
        Assert.Equal("10", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("15", args[args.IndexOf("-t") + 1]);
    }

    [Fact]
    public void Gif_numbers_use_a_point_whatever_the_language_of_Windows()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            var args = Args(1.5, 2.25).ToList();
            Assert.Equal("1.5", args[args.IndexOf("-ss") + 1]);
            Assert.Equal("2.25", args[args.IndexOf("-t") + 1]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Gif_is_small_and_makes_its_own_palette()
    {
        var filter = Filter(Args());
        Assert.StartsWith("fps=15,scale='min(480,iw)':-2", filter);
        Assert.Contains("palettegen", filter);
        Assert.Contains("paletteuse", filter);
    }

    [Fact]
    public void Gif_has_no_sound_loops_forever_and_names_its_format_because_the_output_ends_in_part()
    {
        var args = Args().ToList();
        Assert.Contains("-an", args);
        Assert.Equal("0", args[args.IndexOf("-loop") + 1]);
        Assert.Equal("gif", args[args.IndexOf("-f") + 1]);
        Assert.Equal("out.gif.part", args[^1]);
    }

    [Theory]
    [InlineData(0, 60, 60, 45, 15)]    // The whole clip: its end, where the moment is.
    [InlineData(10, 20, 60, 10, 10)]   // Trim marks shorter than the limit are kept.
    [InlineData(10, 25, 60, 10, 15)]   // Exactly the limit.
    [InlineData(0, 40, 60, 25, 15)]    // Marks longer than the limit keep their end.
    [InlineData(0, 5, 5, 0, 5)]        // A short clip.
    [InlineData(50, 70, 60, 50, 10)]   // A mark past the end.
    [InlineData(-5, 5, 60, 0, 5)]      // A mark before the start.
    [InlineData(0, 0, 60, 45, 15)]     // No marks yet.
    [InlineData(20, 10, 60, 45, 15)]   // Marks that make no sense.
    public void Gif_range_is_the_marks_cut_down_to_their_last_15_seconds(double start, double end, double duration, double wantStart, double wantLength)
    {
        var (from, length) = FfmpegArgs.GifRange(start, end, duration);
        Assert.Equal(wantStart, from, 3);
        Assert.Equal(wantLength, length, 3);
    }

    [Fact]
    public void Gif_goes_next_to_its_clip_under_a_name_of_its_own()
    {
        var clip = Path.Combine("G", "G_2026-10-30_21-14-02.mp4");
        var first = Path.Combine("G", "G_2026-10-30_21-14-02_gif.gif");
        Assert.Equal(first, ClipNaming.DerivedPath(clip, "gif", _ => false, ".gif"));
        Assert.Equal(Path.Combine("G", "G_2026-10-30_21-14-02_gif_2.gif"), ClipNaming.DerivedPath(clip, "gif", p => p == first, ".gif"));
    }

    [Fact]
    public void Other_copies_stay_mp4()
    {
        Assert.Equal(Path.Combine("G", "G_1_trim.mp4"), ClipNaming.DerivedPath(Path.Combine("G", "G_1.mp4"), "trim", _ => false));
    }

    [Theory]
    [InlineData("G_2026-10-30_21-14-02_gif.gif")]
    [InlineData("G_2026-10-30_21-14-02_gif_2.gif")]
    [InlineData("G_2026-10-30_21-14-02_trim_gif.gif")]
    public void Gif_counts_as_made_by_Ghostclip(string name)
    {
        Assert.True(ClipLibrary.IsGhostclipName("G", Path.GetFileNameWithoutExtension(name)));
        Assert.Equal(new DateTime(2026, 10, 30, 21, 14, 2), ClipLibrary.TimeFromName(name));
    }

    [Fact]
    public void Gif_is_listed_with_the_images_so_the_library_shows_it_without_a_timeline()
    {
        var root = Directory.CreateTempSubdirectory("ghostclip-gif-").FullName;
        try
        {
            var game = Directory.CreateDirectory(Path.Combine(root, "G")).FullName;
            File.WriteAllBytes(Path.Combine(game, "G_2026-10-30_21-14-02_gif.gif"), [1]);
            File.WriteAllBytes(Path.Combine(game, "G_2026-10-30_21-14-02_gif.gif.part"), [1]);

            var entry = Assert.Single(ClipLibrary.Scan(root));
            Assert.True(entry.Image);
            Assert.False(entry.Derived);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Gif_left_half_made_is_cleaned_up()
    {
        var root = Directory.CreateTempSubdirectory("ghostclip-gif-").FullName;
        try
        {
            var game = Directory.CreateDirectory(Path.Combine(root, "G")).FullName;
            var part = Path.Combine(game, "G_2026-10-30_21-14-02_gif.gif.part");
            File.WriteAllBytes(part, [1]);
            File.SetLastWriteTimeUtc(part, DateTime.UtcNow.AddDays(-2));

            Assert.Equal(1, PartFile.CleanAbandoned(root, TimeSpan.FromHours(1), DateTime.UtcNow));
            Assert.False(File.Exists(part));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Ffmpeg_build_has_what_a_gif_needs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        string[] Listed(string kind) => flags.Single(l => l.StartsWith($"--enable-{kind}=", StringComparison.Ordinal)).Split('=')[1].Split(',');

        Assert.Contains("gif", Listed("encoder"));
        Assert.Contains("gif", Listed("muxer"));
        foreach (var filter in new[] { "split", "palettegen", "paletteuse" }) Assert.Contains(filter, Listed("filter"));
    }
}
