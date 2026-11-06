using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// The button that checks, on this PC, what the app does to a clip. It makes its own clip and never shows or records the screen.
public class SelfTestTests
{
    [Fact]
    public void Its_clip_is_made_up_with_no_screen_in_it()
    {
        var args = FfmpegArgs.SelfTestClip("out.mp4", "h264_nvenc").ToList();
        var graph = args[args.IndexOf("-filter_complex") + 1];
        Assert.DoesNotContain("ddagrab", graph);
        Assert.DoesNotContain("gfxcapture", graph);
        Assert.DoesNotContain("-i", args);
        Assert.StartsWith("color=", graph);
        Assert.Contains("format=nv12", graph);
    }

    [Fact]
    public void Its_clip_has_the_encoder_chosen_and_two_named_tracks()
    {
        var args = FfmpegArgs.SelfTestClip("out.mp4", "h264_qsv").ToList();
        Assert.Equal("h264_qsv", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal(3, args.Count(a => a == "-map"));
        Assert.Contains("handler_name=Game", args);
        Assert.Contains("handler_name=Microphone", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void The_report_says_each_step_and_how_many_passed()
    {
        var report = SelfTestReport.Format([
            new SelfTestStep("Encoder", true, "h264_nvenc"),
            new SelfTestStep("GIF", false, "Filter not found"),
            new SelfTestStep("Frame", true, null),
        ]);
        Assert.Contains("2/3", report);
        Assert.Contains("[ok] Encoder: h264_nvenc", report);
        Assert.Contains("[FAILED] GIF: Filter not found", report);
        Assert.Contains("[ok] Frame", report);
    }

    [Fact]
    public void An_error_doesnt_carry_where_the_test_ran_or_who_is_using_the_PC()
    {
        var text = SelfTestReport.Clean(@"Error opening C:\Users\Someone\AppData\Local\GhostclipData\SelfTest-1\G.mp4 and C:\Users\Someone\x.txt",
            @"C:\Users\Someone\AppData\Local\GhostclipData\SelfTest-1", @"C:\Users\Someone");
        Assert.Equal(@"Error opening <test folder>\G.mp4 and <home>\x.txt", text);
    }

    [Fact]
    public void Cleaning_ignores_case_and_copes_with_nothing()
    {
        Assert.Equal(@"<home>\a", SelfTestReport.Clean(@"c:\users\someone\a", @"C:\Other", @"C:\Users\Someone"));
        Assert.Null(SelfTestReport.Clean(null, "a", "b"));
    }

    [Fact]
    public void A_long_error_is_cut()
    {
        Assert.True(SelfTestReport.Clean(new string('x', 2000), "a", "b")!.Length <= 300);
    }

    [Fact]
    public void Steps_reach_the_page()
    {
        var json = JsonSerializer.Serialize(new LibraryEvent("selfTest", Steps: [new SelfTestStep("GIF", true, "ok")]), LibraryJson.Default.LibraryEvent);
        Assert.Contains("\"steps\":[{\"name\":\"GIF\",\"ok\":true,\"detail\":\"ok\"}]", json);
    }

    [Fact]
    public void Ffmpeg_build_has_what_its_clip_needs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ghostclip.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var flags = File.ReadAllLines(Path.Combine(dir.FullName, "build", "ffmpeg", "configure-flags.txt"));
        var filters = flags.Single(l => l.StartsWith("--enable-filter=", StringComparison.Ordinal)).Split('=')[1].Split(',');
        var verify = File.ReadAllText(Path.Combine(dir.FullName, "build", "ffmpeg", "verify.ps1"));
        foreach (var filter in new[] { "color", "sine" })
        {
            Assert.Contains(filter, filters);
            Assert.Contains($"'{filter}'", verify);
        }
    }
}
