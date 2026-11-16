namespace Ghostclip.Core;

/// <summary>One process's CPU time so far and memory now.</summary>
public sealed record CostSample(int ProcessId, TimeSpan Cpu, long MemoryBytes);

/// <summary>
/// What recording uses now: CPU as a share of the whole machine, as Task Manager shows it, memory, and how fast
/// the replay buffer is written. Null where there's nothing to say yet.
/// </summary>
public sealed record LiveCost(double? CpuPercent, long MemoryBytes, double? DiskMBPerMinute);

public static class CostMeter
{
    private const int RecentSegments = 5;

    /// <summary>Only processes seen both times count: one that started or ended in between has no span to measure.</summary>
    public static double CpuPercent(IReadOnlyList<CostSample> before, IReadOnlyList<CostSample> after, TimeSpan elapsed, int cores)
    {
        if (elapsed <= TimeSpan.Zero || cores <= 0) return 0;
        var earlier = before.ToDictionary(s => s.ProcessId, s => s.Cpu);
        var used = after.Where(s => earlier.ContainsKey(s.ProcessId))
            .Sum(s => Math.Max(0, (s.Cpu - earlier[s.ProcessId]).TotalSeconds));
        return used / (elapsed.TotalSeconds * cores) * 100;
    }

    /// <summary>
    /// From the last closed segments: the newest is still being written. Null when there aren't two, or nothing has
    /// been written for a while, as when recording is paused.
    /// </summary>
    public static double? DiskMBPerMinute(IReadOnlyList<SegmentFile> segments, DateTime nowUtc, int segmentSeconds)
    {
        if (segments.Count < 2 || segmentSeconds <= 0) return null;
        var ordered = segments.OrderBy(s => s.Sequence).ToList();
        if (nowUtc - ordered[^1].LastWriteUtc > TimeSpan.FromSeconds(segmentSeconds * 5)) return null;
        var closed = ordered.Take(ordered.Count - 1).TakeLast(RecentSegments).ToList();
        var bytesPerSecond = closed.Average(s => (double)s.Length) / segmentSeconds;
        return bytesPerSecond * 60 / 1024 / 1024;
    }
}
