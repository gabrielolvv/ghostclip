using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Which buffer segments a session recording takes, across recorder restarts.
public class SessionPlanTests
{
    private static readonly DateTime T0 = new(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Closed_segments_from_the_start_are_linked_and_the_one_being_written_waits()
    {
        var link = SessionPlan.ToLink([8, 9, 10, 11, 12], from: 10, linked: new HashSet<long> { 10 }, final: false);
        Assert.Equal([11], link);
    }

    [Fact]
    public void At_the_end_the_segment_being_written_is_taken_too()
    {
        var link = SessionPlan.ToLink([10, 11, 12], from: 10, linked: new HashSet<long> { 10 }, final: true);
        Assert.Equal([11, 12], link);
    }

    [Fact]
    public void A_part_after_a_restart_starts_at_the_first_segment_created_since()
    {
        var segments = new[] { (500L, T0.AddSeconds(-4)), (501L, T0.AddSeconds(-2)), (502L, T0.AddSeconds(3)), (503L, T0.AddSeconds(5)) };
        Assert.Equal(502, SessionPlan.FirstAfter(segments, T0));
    }

    [Fact]
    public void A_restart_that_numbers_from_zero_again_is_still_followed()
    {
        // The screen was locked long enough for the recorder to clear the buffer: numbering starts over.
        var segments = new[] { (0L, T0.AddMinutes(10)), (1L, T0.AddMinutes(10).AddSeconds(2)) };
        Assert.Equal(0, SessionPlan.FirstAfter(segments, T0));
    }

    [Fact]
    public void Until_the_new_run_writes_a_segment_there_is_nothing_to_take()
    {
        var segments = new[] { (500L, T0.AddSeconds(-4)), (501L, T0.AddSeconds(-2)) };
        Assert.Null(SessionPlan.FirstAfter(segments, T0));
    }
}
