using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class AudioSyncTests
{
    [Fact]
    public void Lead_is_audio_time_minus_video_produced()
    {
        // Measured on an Intel UHD: first report 1.005 s after audio start, 50 frames at 60 fps.
        Assert.Equal(155, AudioSync.MeasureLeadMs(1.005, 50, 60));
    }

    [Fact]
    public void Absurd_measurements_are_clamped()
    {
        Assert.Equal(2000, AudioSync.MeasureLeadMs(30, 1, 60));
        Assert.Equal(-1000, AudioSync.MeasureLeadMs(0, 600, 60));
    }

    [Fact]
    public void Segment_belongs_to_the_newest_session_that_started_before_it()
    {
        RecordingSession[] sessions = [new(0, 150), new(25, 170), new(60, 160)];
        Assert.Equal(150, AudioSync.ForSequence(sessions, 24)?.AudioLeadMs);
        Assert.Equal(170, AudioSync.ForSequence(sessions, 25)?.AudioLeadMs);
        Assert.Equal(160, AudioSync.ForSequence(sessions, 99)?.AudioLeadMs);
        Assert.Null(AudioSync.ForSequence([new(10, 1)], 5));
    }

    [Fact]
    public void Only_sessions_without_segments_are_obsolete()
    {
        RecordingSession[] sessions = [new(0, 1), new(25, 2), new(60, 3)];
        // Oldest segment is 30: it belongs to session 25, so only session 0 can go.
        Assert.Equal([0L], AudioSync.SelectObsolete(sessions, 30).Select(s => s.StartSequence));
    }

    [Fact]
    public void Sessions_round_trip_through_the_buffer_folder()
    {
        var dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;
        try
        {
            AudioSync.Write(dir, new RecordingSession(42, 168));
            AudioSync.Write(dir, new RecordingSession(7, 150));
            File.WriteAllText(Path.Combine(dir, "session_00000099.json"), "{ broken");
            Assert.Equal([new RecordingSession(7, 150), new RecordingSession(42, 168)], AudioSync.Scan(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Remux_shifts_audio_from_a_second_read_of_the_list()
    {
        var args = FfmpegArgs.Remux("list.ffconcat", "out.mp4", 170).ToList();
        Assert.Equal(2, args.Count(a => a == "list.ffconcat"));
        Assert.Equal("-0.170", args[args.IndexOf("-itsoffset") + 1]);
        Assert.Contains("1:a?", args);
        Assert.Equal("copy", args[args.IndexOf("-c") + 1]);
        Assert.DoesNotContain("-itsoffset", FfmpegArgs.Remux("list.ffconcat", "out.mp4"));
    }
}
