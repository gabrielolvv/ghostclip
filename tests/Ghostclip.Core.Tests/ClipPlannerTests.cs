using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class ClipPlannerTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>Segment n closes at 2*(n+1) s; the one being written was last touched at <paramref name="currentAt"/>.</summary>
    private static List<SegmentFile> Buffer(int closed, double currentAt)
    {
        var list = Enumerable.Range(0, closed)
            .Select(i => new SegmentFile($"seg_{i}.ts", i, 100, T0.AddSeconds(2 * (i + 1))))
            .ToList();
        list.Add(new SegmentFile($"seg_{closed}.ts", closed, 50, T0.AddSeconds(currentAt)));
        return list;
    }

    [Fact]
    public void Picks_just_enough_segments_and_includes_the_current_one()
    {
        // 40 closed segments, current one 0.5 s in.
        var plan = ClipPlanner.Plan(Buffer(40, 80.5), clipSeconds: 60, segmentSeconds: 2, recording: true);

        Assert.Equal(40, plan.Current?.Sequence);
        Assert.Equal(30, plan.Complete.Count);
        Assert.Equal(10, plan.Complete[0].Sequence);
        Assert.Equal(39, plan.Complete[^1].Sequence);
        Assert.InRange(plan.EstimatedSeconds, 60, 62);
    }

    [Fact]
    public void Segments_come_out_oldest_first()
    {
        var plan = ClipPlanner.Plan(Buffer(10, 21), 60, 2, recording: true);
        Assert.Equal(plan.Complete.Select(s => s.Sequence).Order(), plan.Complete.Select(s => s.Sequence));
    }

    [Fact]
    public void Short_buffer_gives_everything_it_has()
    {
        var plan = ClipPlanner.Plan(Buffer(5, 11), 60, 2, recording: true);
        Assert.Equal(5, plan.Complete.Count);
        Assert.InRange(plan.EstimatedSeconds, 10, 12);
    }

    [Fact]
    public void Without_a_recorder_the_newest_segment_counts_as_complete()
    {
        // Worker down: nothing is being written, so the newest file is closed.
        var plan = ClipPlanner.Plan(Buffer(40, 82), 60, 2, recording: false);
        Assert.Null(plan.Current);
        Assert.Equal(30, plan.Complete.Count);
        Assert.Equal(40, plan.Complete[^1].Sequence);
    }

    [Fact]
    public void Ordering_uses_sequence_not_file_time()
    {
        // After a restart the sequence continues; times may jump.
        var segments = new List<SegmentFile>
        {
            new("b.ts", 5, 100, T0.AddSeconds(100)),
            new("a.ts", 4, 100, T0.AddSeconds(10)),
            new("c.ts", 6, 100, T0.AddSeconds(101)),
        };
        var plan = ClipPlanner.Plan(segments, 60, 2, recording: false);
        Assert.Equal([4L, 5L, 6L], plan.Complete.Select(s => s.Sequence));
    }

    [Fact]
    public void Empty_files_are_skipped()
    {
        var segments = new List<SegmentFile> { new("a.ts", 1, 100, T0), new("b.ts", 2, 0, T0.AddSeconds(2)) };
        var plan = ClipPlanner.Plan(segments, 60, 2, recording: false);
        Assert.Equal([1L], plan.Complete.Select(s => s.Sequence));
    }

    [Fact]
    public void Empty_buffer_gives_empty_plan() =>
        Assert.True(ClipPlanner.Plan([], 60, 2, recording: true).IsEmpty);

    [Fact]
    public void Only_the_current_segment_is_still_a_clip()
    {
        var plan = ClipPlanner.Plan([new SegmentFile("a.ts", 0, 10, T0)], 60, 2, recording: true);
        Assert.Empty(plan.Complete);
        Assert.NotNull(plan.Current);
        Assert.False(plan.IsEmpty);
    }
}
