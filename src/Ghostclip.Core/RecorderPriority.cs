using System.Diagnostics;

namespace Ghostclip.Core;

/// <summary>Windows' GPU scheduling priority classes (D3DKMT_SCHEDULINGPRIORITYCLASS).</summary>
public enum GpuPriorityClass { Idle = 0, BelowNormal = 1, Normal = 2, AboveNormal = 3, High = 4, Realtime = 5 }

/// <summary>
/// How the recording FFmpeg shares the PC with the game. Capture, color conversion and
/// encoding run on the game's GPU, and at normal priority they compete with it as equals.
/// With games first (<see cref="GhostclipConfig.GamePriority"/>, the default) the recorder
/// runs below normal on the CPU and on the GPU, so the game's frames go ahead of it; the
/// cost is that a GPU loaded to the limit can make a clip drop frames.
/// </summary>
public readonly record struct RecorderPriority(ProcessPriorityClass Cpu, GpuPriorityClass Gpu)
{
    public static RecorderPriority For(bool gamesFirst) => gamesFirst
        ? new(ProcessPriorityClass.BelowNormal, GpuPriorityClass.BelowNormal)
        : new(ProcessPriorityClass.Normal, GpuPriorityClass.Normal);
}
