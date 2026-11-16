using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// What Ghostclip uses while it records, shown in the library as it happens.
public class LiveCostTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static CostSample Sample(int pid, double cpuSeconds, long mb = 100) => new(pid, TimeSpan.FromSeconds(cpuSeconds), mb * 1024 * 1024);

    [Fact]
    public void CPU_is_the_time_used_over_the_time_passed_across_every_core()
    {
        // 0.4 s of CPU across two processes in 2 s, on 4 cores: 5% of the machine.
        var before = new[] { Sample(1, 10), Sample(2, 20) };
        var after = new[] { Sample(1, 10.1), Sample(2, 20.3) };
        Assert.Equal(5, CostMeter.CpuPercent(before, after, TimeSpan.FromSeconds(2), 4), 3);
    }

    [Fact]
    public void A_process_that_started_or_ended_between_two_looks_isnt_counted_for_that_look()
    {
        var before = new[] { Sample(1, 10), Sample(2, 20) };
        var after = new[] { Sample(1, 10.2), Sample(3, 99) };
        Assert.Equal(10, CostMeter.CpuPercent(before, after, TimeSpan.FromSeconds(2), 1), 3);
    }

    [Fact]
    public void A_pid_reused_by_another_program_doesnt_count_backwards()
    {
        Assert.Equal(0, CostMeter.CpuPercent([Sample(1, 50)], [Sample(1, 1)], TimeSpan.FromSeconds(2), 4));
    }

    [Fact]
    public void Nothing_passed_or_no_cores_is_zero()
    {
        Assert.Equal(0, CostMeter.CpuPercent([Sample(1, 1)], [Sample(1, 2)], TimeSpan.Zero, 4));
        Assert.Equal(0, CostMeter.CpuPercent([Sample(1, 1)], [Sample(1, 2)], TimeSpan.FromSeconds(1), 0));
    }

    private static SegmentFile Segment(long sequence, long bytes, double secondsAgo) =>
        new($"seg_{sequence}.ts", sequence, bytes, Now.AddSeconds(-secondsAgo));

    [Fact]
    public void Disk_is_the_closed_segments_per_minute()
    {
        // 5 MB every 2 s is 150 MB a minute. The newest one is still being written and doesn't count.
        var mb = 1024L * 1024;
        var segments = new[] { Segment(1, 5 * mb, 8), Segment(2, 5 * mb, 6), Segment(3, 5 * mb, 4), Segment(4, 5 * mb, 2), Segment(5, 1 * mb, 0) };
        Assert.Equal(150, CostMeter.DiskMBPerMinute(segments, Now, 2)!.Value, 3);
    }

    [Fact]
    public void Only_the_last_few_count_so_a_change_shows_soon()
    {
        var mb = 1024L * 1024;
        var segments = Enumerable.Range(1, 20).Select(i => Segment(i, (i <= 10 ? 1 : 10) * mb, 40 - i * 2)).ToList();
        Assert.Equal(300, CostMeter.DiskMBPerMinute(segments, Now, 2)!.Value, 3);
    }

    [Fact]
    public void Not_recording_has_no_rate()
    {
        var mb = 1024L * 1024;
        Assert.Null(CostMeter.DiskMBPerMinute([], Now, 2));
        Assert.Null(CostMeter.DiskMBPerMinute([Segment(1, mb, 1)], Now, 2));
        // Nothing written for a while: paused, or the recorder stopped.
        Assert.Null(CostMeter.DiskMBPerMinute([Segment(1, mb, 30), Segment(2, mb, 28)], Now, 2));
    }

    [Fact]
    public void It_reaches_the_page()
    {
        var json = JsonSerializer.Serialize(new LibraryEvent("cost", Cost: new LiveCost(1.25, 200 * 1024 * 1024, 150)), LibraryJson.Default.LibraryEvent);
        Assert.Contains("\"cost\":{\"cpuPercent\":1.25,\"memoryBytes\":209715200,\"diskMBPerMinute\":150}", json);
    }
}
