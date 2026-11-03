using System.Runtime.InteropServices;
using Ghostclip.Core;

namespace Ghostclip.Worker.Native;

/// <summary>
/// A process's GPU scheduling priority: the GPU counterpart of its CPU priority class.
/// Lowering it needs no privilege; raising it above normal would.
/// </summary>
internal static partial class GpuScheduling
{
    /// <summary>Sets it; false, with the NTSTATUS, when Windows or the driver refuses.</summary>
    public static bool TrySet(nint process, GpuPriorityClass priority, out int status)
    {
        status = D3DKMTSetProcessSchedulingPriorityClass(process, (int)priority);
        return status == 0;
    }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTSetProcessSchedulingPriorityClass(nint process, int priority);
}
