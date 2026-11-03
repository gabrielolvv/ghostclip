using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Ghostclip.Core;

namespace Ghostclip.Native;

// Compiled into both the tray (the settings page lists the monitors) and the worker (Graphics Capture needs
// the monitor behind an output). DXGI's first adapter is the one FFmpeg's d3d11va device and ddagrab open
// (unless Windows' per-app GPU preference picks another for one of the programs), so its outputs keep the
// indexes ddagrab records. Source-generated COM interop, like WASAPI; methods are declared in vtable order and
// only the ones used are called.

[GeneratedComInterface]
[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
internal partial interface IDXGIFactory1
{
    // IDXGIObject
    [PreserveSig] int SetPrivateData();
    [PreserveSig] int SetPrivateDataInterface();
    [PreserveSig] int GetPrivateData();
    [PreserveSig] int GetParent();
    // IDXGIFactory
    [PreserveSig] int EnumAdapters(uint index, out IDXGIAdapter? adapter);
}

[GeneratedComInterface]
[Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0")]
internal partial interface IDXGIAdapter
{
    // IDXGIObject
    [PreserveSig] int SetPrivateData();
    [PreserveSig] int SetPrivateDataInterface();
    [PreserveSig] int GetPrivateData();
    [PreserveSig] int GetParent();
    // IDXGIAdapter
    [PreserveSig] int EnumOutputs(uint index, out IDXGIOutput? output);
}

[GeneratedComInterface]
[Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa")]
internal partial interface IDXGIOutput
{
    // IDXGIObject
    [PreserveSig] int SetPrivateData();
    [PreserveSig] int SetPrivateDataInterface();
    [PreserveSig] int GetPrivateData();
    [PreserveSig] int GetParent();
    // IDXGIOutput
    [PreserveSig] int GetDesc(out Displays.OutputDesc desc);
}

internal static unsafe partial class Displays
{
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const int DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;

    private static readonly StrategyBasedComWrappers Com = new();

    /// <summary>The monitors Ghostclip can record, named after what Windows calls them.</summary>
    public static IReadOnlyList<MonitorInfo> List() => MonitorList.Build(Outputs().Select(o => o.Output), Targets());

    /// <summary>The HMONITOR behind output <paramref name="index"/> (as <see cref="List"/> numbers them); 0 when there is none.</summary>
    public static (long Handle, string? Device) Handle(int index) =>
        Outputs().FirstOrDefault(o => o.Output.Index == index) is ({ DeviceName: { } device }, var monitor) ? (monitor, device) : (0, null);

    // The first GPU's outputs, numbered as ddagrab numbers them, then every other GPU's, which only Graphics
    // Capture can record (a gaming laptop's HDMI port often belongs to the dedicated GPU).
    private static List<(DisplayOutput Output, long Monitor)> Outputs()
    {
        var outputs = new List<(DisplayOutput, long)>();
        var iid = typeof(IDXGIFactory1).GUID;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(iid, out var ptr));
        IDXGIFactory1 factory;
        try
        {
            factory = (IDXGIFactory1)Com.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ptr);
        }
        try
        {
            for (uint a = 0; ; a++)
            {
                var hr = factory.EnumAdapters(a, out var adapter);
                if (hr == DXGI_ERROR_NOT_FOUND) break;
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    for (uint i = 0; adapter!.EnumOutputs(i, out var output) == 0 && output is not null; i++)
                    {
                        try
                        {
                            if (output.GetDesc(out var desc) != 0) continue;
                            var info = new MONITORINFO { cbSize = sizeof(MONITORINFO) };
                            var primary = GetMonitorInfoW(desc.Monitor, ref info) && (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                            var r = desc.DesktopCoordinates;
                            outputs.Add((new DisplayOutput(outputs.Count, new string(desc.DeviceName), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                                primary, OtherGpu: a > 0), desc.Monitor));
                        }
                        finally
                        {
                            Release(output);
                        }
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
        return outputs;
    }

    private static void Release(object? com)
    {
        if (com is ComObject c) c.FinalRelease();
    }

    // The monitors behind each GDI device, with their friendly names and current modes.
    private static List<DisplayTarget> Targets()
    {
        const int ERROR_INSUFFICIENT_BUFFER = 122;
        var targets = new List<DisplayTarget>();
        PathInfo[] paths;
        ModeInfo[] modes;
        uint pathCount, modeCount;
        // A screen plugged in between the two calls makes the buffers too small: ask again.
        for (var attempt = 0; ; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0) return targets;
            paths = new PathInfo[pathCount];
            modes = new ModeInfo[modeCount];
            int result;
            fixed (PathInfo* p = paths)
            fixed (ModeInfo* m = modes)
            {
                result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, p, ref modeCount, m, 0);
            }
            if (result == 0) break;
            if (result != ERROR_INSUFFICIENT_BUFFER || attempt == 2) return targets;
        }
        foreach (var path in paths.AsSpan(0, (int)pathCount))
        {
            var source = new SourceDeviceName { Header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, sizeof(SourceDeviceName), path.SourceAdapter, path.SourceId) };
            if (DisplayConfigGetDeviceInfo(&source.Header) != 0) continue;
            var target = new TargetDeviceName { Header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, sizeof(TargetDeviceName), path.TargetAdapter, path.TargetId) };
            var named = DisplayConfigGetDeviceInfo(&target.Header) == 0;
            var tech = path.OutputTechnology;
            var builtIn = tech is DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
                or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;
            var (width, height) = path.SourceModeIndex < modeCount && modes[path.SourceModeIndex].InfoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE
                ? ((int)modes[path.SourceModeIndex].Width, (int)modes[path.SourceModeIndex].Height)
                : (0, 0);
            targets.Add(new DisplayTarget(new string(source.ViewGdiDeviceName), named ? new string(target.MonitorFriendlyDeviceName) : null, builtIn, width, height));
        }
        return targets;
    }

    private static DeviceInfoHeader Header(int type, int size, Luid adapter, uint id) =>
        new() { Type = type, Size = size, AdapterId = adapter, Id = id };

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OutputDesc
    {
        public fixed char DeviceName[32];
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public nint Monitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    // DISPLAYCONFIG_PATH_INFO, 72 bytes: source info, target info, flags.
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Luid SourceAdapter;
        public uint SourceId;
        public uint SourceModeIndex;
        public uint SourceStatus;
        public Luid TargetAdapter;
        public uint TargetId;
        public uint TargetModeIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshNumerator;
        public uint RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatus;
        public uint Flags;
    }

    // DISPLAYCONFIG_MODE_INFO, 64 bytes; for a source mode the union starts with its width and height.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct ModeInfo
    {
        [FieldOffset(0)] public int InfoType;
        [FieldOffset(16)] public uint Width;
        [FieldOffset(20)] public uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader
    {
        public int Type;
        public int Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SourceDeviceName
    {
        public DeviceInfoHeader Header;
        public fixed char ViewGdiDeviceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char MonitorFriendlyDeviceName[64];
        public fixed char MonitorDevicePath[128];
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid iid, out nint factory);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    private static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    private static partial int QueryDisplayConfig(uint flags, ref uint pathCount, PathInfo* paths, ref uint modeCount, ModeInfo* modes, nint topologyId);

    [LibraryImport("user32.dll")]
    private static partial int DisplayConfigGetDeviceInfo(DeviceInfoHeader* request);
}
