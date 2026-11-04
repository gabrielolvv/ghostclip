using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// A warning before a save fails, not one every minute until the disk is cleaned.
public class LowDiskWatchTests
{
    private const long GB = 1024L * 1024 * 1024;

    [Fact]
    public void The_threshold_is_at_least_two_gigabytes()
    {
        Assert.Equal(2 * GB, LowDiskWatch.Threshold(new GhostclipConfig()));
    }

    [Fact]
    public void It_grows_with_what_the_buffer_holds()
    {
        // 150 Mbps for 10 minutes is about 11 GB of buffer; room for that three times over.
        var config = new GhostclipConfig { BitrateMbps = 150, ClipSeconds = 600 };
        Assert.Equal(3 * 150 * 125_000L * 600, LowDiskWatch.Threshold(config));
    }

    [Fact]
    public void A_drive_with_room_says_nothing()
    {
        Assert.False(new LowDiskWatch().ShouldWarn(@"C:\", 50 * GB, 2 * GB));
    }

    [Fact]
    public void A_low_drive_warns_once()
    {
        var watch = new LowDiskWatch();
        Assert.True(watch.ShouldWarn(@"C:\", GB, 2 * GB));
        Assert.False(watch.ShouldWarn(@"C:\", GB, 2 * GB));
        Assert.False(watch.ShouldWarn(@"c:\", GB / 2, 2 * GB));
    }

    [Fact]
    public void Each_drive_warns_for_itself()
    {
        var watch = new LowDiskWatch();
        Assert.True(watch.ShouldWarn(@"C:\", GB, 2 * GB));
        Assert.True(watch.ShouldWarn(@"D:\", GB, 2 * GB));
    }

    [Fact]
    public void Hovering_around_the_threshold_doesn_t_warn_again()
    {
        var watch = new LowDiskWatch();
        Assert.True(watch.ShouldWarn(@"C:\", GB, 2 * GB));
        Assert.False(watch.ShouldWarn(@"C:\", 3 * GB, 2 * GB));
        Assert.False(watch.ShouldWarn(@"C:\", GB, 2 * GB));
    }

    [Fact]
    public void After_real_room_it_can_warn_again()
    {
        var watch = new LowDiskWatch();
        Assert.True(watch.ShouldWarn(@"C:\", GB, 2 * GB));
        Assert.False(watch.ShouldWarn(@"C:\", 4 * GB, 2 * GB));
        Assert.True(watch.ShouldWarn(@"C:\", GB, 2 * GB));
    }
}
