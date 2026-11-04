using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A Discord size copy with no sound has that much more room for the picture.
public class SilentCopyTests
{
    [Fact]
    public void Without_sound_the_video_gets_the_audio_s_share()
    {
        var withSound = CompressionPlanner.Plan(60, 1080, 60, 10)!;
        var silent = CompressionPlanner.Plan(60, 1080, 60, 10, silent: true)!;
        Assert.Equal(withSound.VideoKbps + CompressionPlanner.AudioKbps, silent.VideoKbps);
        Assert.Equal(0, silent.AudioKbps);
    }

    [Fact]
    public void A_clip_too_long_with_sound_can_fit_without_it()
    {
        Assert.Null(CompressionPlanner.Plan(250, 1080, 60, 10));
        Assert.NotNull(CompressionPlanner.Plan(250, 1080, 60, 10, silent: true));
    }

    [Fact]
    public void With_sound_nothing_changes()
    {
        Assert.Equal(CompressionPlanner.AudioKbps, CompressionPlanner.Plan(60, 1080, 60, 10)!.AudioKbps);
    }

    [Fact]
    public void The_retry_stays_silent_too()
    {
        var retry = CompressionPlanner.Plan(60, 1080, 60, 10, CompressionPlanner.RetrySafety(12 * 1024 * 1024, 10), silent: true)!;
        Assert.Equal(0, retry.AudioKbps);
    }
}
