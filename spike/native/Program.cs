// Phase 2 spike: native capture and encode, to compare its cost with FFmpeg.
//
// Desktop Duplication at the clip rate -> D3D11 VideoProcessor (BGRA -> NV12)
// into a fixed pool of textures -> Media Foundation hardware H.264 encoder.
// The encoded output is counted and thrown away. Throwaway code: no error
// recovery, no muxing, no audio.
//
// Usage: NativeSpike [seconds] [fps] [mbps]

using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

var seconds = args.Length > 0 ? int.Parse(args[0]) : 30;
var fps = args.Length > 1 ? int.Parse(args[1]) : 60;
var mbps = args.Length > 2 ? int.Parse(args[2]) : 20;
const int PoolSize = 4;

Console.WriteLine($"pid {Environment.ProcessId}");
Console.WriteLine($"mem after start: private {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, ws {Environment.WorkingSet / 1048576} MB");

// --- D3D11 device on the adapter that owns monitor 0 ---
using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
factory.EnumAdapters1(0, out var adapter).CheckError();
adapter.EnumOutputs(0, out var output).CheckError();
using var output1 = output.QueryInterface<IDXGIOutput1>();
var bounds = output.Description.DesktopCoordinates;
var width = (uint)(bounds.Right - bounds.Left);
var height = (uint)(bounds.Bottom - bounds.Top);
Console.WriteLine($"adapter: {adapter.Description1.Description}, output {width}x{height}");

D3D11.D3D11CreateDevice(adapter, DriverType.Unknown,
    DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
    [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
    out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
using (var mt = device!.QueryInterface<ID3D11Multithread>()) mt.SetMultithreadProtected(true);

using var duplication = output1.DuplicateOutput(device);
Console.WriteLine($"mem after D3D11 device + duplication: private {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, ws {Environment.WorkingSet / 1048576} MB");

// --- Video processor: BGRA desktop -> NV12 pool ---
using var videoDevice = device.QueryInterface<ID3D11VideoDevice>();
using var videoContext = context!.QueryInterface<ID3D11VideoContext>();
var content = new VideoProcessorContentDescription
{
    InputFrameFormat = VideoFrameFormat.Progressive,
    InputFrameRate = new Rational((uint)fps, 1),
    InputWidth = width,
    InputHeight = height,
    OutputFrameRate = new Rational((uint)fps, 1),
    OutputWidth = width,
    OutputHeight = height,
    Usage = VideoUsage.OptimalSpeed,
};
videoDevice.CreateVideoProcessorEnumerator(ref content, out var vpEnum).CheckError();
videoDevice.CreateVideoProcessor(vpEnum, 0, out var processor).CheckError();
Console.WriteLine($"mem after video processor: private {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, ws {Environment.WorkingSet / 1048576} MB");

var pool = new ID3D11Texture2D[PoolSize];
var outViews = new ID3D11VideoProcessorOutputView[PoolSize];
var buffers = new IMFMediaBuffer[PoolSize];

// --- Media Foundation hardware encoder ---
MediaFactory.MFStartup(useLightVersion: false).CheckError();
var nv12 = VideoFormatGuids.FromFourCC(new FourCC("NV12"));
var activates = MediaFactory.MFTEnumEx(TransformCategoryGuids.VideoEncoder,
    (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter),
    new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = nv12 },
    new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 });
var activate = activates.FirstOrDefault();
if (activate is null) { Console.WriteLine("no hardware H.264 encoder"); return 1; }
try { Console.WriteLine($"encoder: {activate.GetAllocatedString(TransformAttributeKeys.MftFriendlyNameAttribute)}"); } catch (SharpGenException) { }
var encoder = activate.ActivateObject<IMFTransform>();
Console.WriteLine($"mem after MFT activate: private {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, ws {Environment.WorkingSet / 1048576} MB");

using (var attrs = encoder.Attributes) attrs.Set(TransformAttributeKeys.TransformAsyncUnlock, 1u);
var deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
deviceManager.ResetDevice(device).CheckError();
encoder.ProcessMessage(TMessageType.MessageSetD3DManager, (UIntPtr)(ulong)deviceManager.NativePointer);

using (var outType = MediaFactory.MFCreateMediaType())
{
    outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
    outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
    outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(mbps * 1_000_000));
    outType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | height);
    outType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)(uint)fps << 32) | 1);
    outType.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1);
    outType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u); // progressive
    encoder.SetOutputType(0, outType, 0);
}
using (var inType = MediaFactory.MFCreateMediaType())
{
    inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
    inType.Set(MediaTypeAttributeKeys.Subtype, nv12);
    inType.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | height);
    inType.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)(uint)fps << 32) | 1);
    inType.Set(MediaTypeAttributeKeys.PixelAspectRatio, (1UL << 32) | 1);
    inType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
    encoder.SetInputType(0, inType, 0);
}

// Pool: NV12 textures the processor writes and the encoder reads, each with a reusable sample.
for (var i = 0; i < PoolSize; i++)
{
    pool[i] = device.CreateTexture2D(new Texture2DDescription
    {
        Width = width,
        Height = height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.NV12,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.VideoEncoder,
    });
    videoDevice.CreateVideoProcessorOutputView(pool[i], vpEnum, new VideoProcessorOutputViewDescription
    {
        ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
        Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
    }, out outViews[i]).CheckError();
    buffers[i] = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, pool[i], 0, false);
    using (var buffer2d = buffers[i].QueryInterface<IMF2DBuffer>()) buffers[i].CurrentLength = buffer2d.ContiguousLength;
}

Console.WriteLine($"mem after media types + NV12 pool: private {System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, ws {Environment.WorkingSet / 1048576} MB");
using var events = encoder.QueryInterface<IMFMediaEventGenerator>();
encoder.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
encoder.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);

// --- Encoder events on their own thread: blocking GetEvent, no exceptions per frame ---
var needInput = new SemaphoreSlim(0);
long encodedBytes = 0, encodedFrames = 0;
var running = true;
var eventThread = new Thread(() =>
{
    while (Volatile.Read(ref running))
    {
        using var ev = events.GetEvent(0);
        var type = ev.EventType;
        if (type != MediaEventTypes.TransformNeedInput && type != MediaEventTypes.TransformHaveOutput)
        {
            Console.WriteLine($"event {type} status 0x{ev.Status.Code:X8}");
        }
        if (type == MediaEventTypes.TransformNeedInput)
        {
            needInput.Release();
        }
        else if (type == MediaEventTypes.TransformHaveOutput)
        {
            var data = new OutputDataBuffer { StreamID = 0 };
            encoder.ProcessOutput(ProcessOutputFlags.None, 1, ref data, out _);
            if (data.Sample is { } s)
            {
                encodedBytes += s.TotalLength;
                encodedFrames++;
                s.Dispose();
            }
            data.Events?.Dispose();
        }
    }
}) { IsBackground = true, Name = "mft-events" };
eventThread.Start();

// --- Capture loop, paced by a high-resolution waitable timer ---
var timer = Native.CreateWaitableTimerExW(0, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003);
var inputViews = new Dictionary<nint, ID3D11VideoProcessorInputView>();
var stream = new VideoProcessorStream[1];
var current = -1;
var next = 0;
long ticks = 0, newFrames = 0, submitted = 0, dropped = 0;
var frameTicks = Stopwatch.Frequency / fps;

var me = Process.GetCurrentProcess();
var cpuStart = me.TotalProcessorTime;
var allocStart = GC.GetTotalAllocatedBytes(precise: true);
var gcStart = GC.CollectionCount(0);
var sw = Stopwatch.StartNew();
var deadline = sw.ElapsedTicks + seconds * Stopwatch.Frequency;
var due = sw.ElapsedTicks;
long peakWs = 0;

while (sw.ElapsedTicks < deadline)
{
    due += frameTicks;
    var wait = due - sw.ElapsedTicks;
    if (wait > 0)
    {
        var relative = -(wait * 10_000_000 / Stopwatch.Frequency);
        Native.SetWaitableTimer(timer, ref relative, 0, 0, 0, false);
        Native.WaitForSingleObject(timer, 1000);
    }
    ticks++;

    // Only the latest desktop image matters: a 144 Hz monitor still costs 60 blits.
    var result = duplication.AcquireNextFrame(0, out _, out var resource);
    if (result.Success && resource is not null)
    {
        using (var texture = resource.QueryInterface<ID3D11Texture2D>())
        {
            if (!inputViews.TryGetValue(texture.NativePointer, out var view))
            {
                videoDevice.CreateVideoProcessorInputView(texture, vpEnum, new VideoProcessorInputViewDescription
                {
                    FourCC = 0,
                    ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
                }, out view).CheckError();
                inputViews[texture.NativePointer] = view;
            }
            stream[0] = new VideoProcessorStream { Enable = true, InputSurface = view };
            videoContext.VideoProcessorBlt(processor, outViews[next], 0, 1, stream).CheckError();
        }
        resource.Dispose();
        duplication.ReleaseFrame();
        current = next;
        next = (next + 1) % PoolSize;
        newFrames++;
    }
    if (current < 0) continue;

    // Constant frame rate: an unchanged desktop resubmits the last image.
    if (needInput.Wait(0))
    {
        // A fresh sample per submission: the encoder may still hold the previous one.
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffers[current]);
        sample.SampleTime = submitted * 10_000_000 / fps;
        sample.SampleDuration = 10_000_000 / fps;
        try
        {
            encoder.ProcessInput(0, sample, 0);
            submitted++;
        }
        catch (SharpGenException ex)
        {
            if (submitted < 3 || dropped < 3) Console.WriteLine($"ProcessInput: 0x{ex.HResult:X8} {ex.Message}");
            dropped++;
        }
    }
    else
    {
        dropped++;
    }
    if (ticks % fps == 0) peakWs = Math.Max(peakWs, Environment.WorkingSet);
}

var elapsed = sw.Elapsed.TotalSeconds;
me.Refresh();
var cpu = (me.TotalProcessorTime - cpuStart).TotalSeconds / elapsed / Environment.ProcessorCount * 100;
var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocStart;
Console.WriteLine($"ticks {ticks}, new desktop frames {newFrames}, submitted {submitted}, dropped {dropped}, encoded {encodedFrames}");
Console.WriteLine($"bitrate {encodedBytes * 8 / elapsed / 1e6:0.0} Mbps");
Console.WriteLine($"cpu {cpu:0.00}% of the machine, working set {me.WorkingSet64 / 1048576.0:0} MB (peak {peakWs / 1048576.0:0}), private {me.PrivateMemorySize64 / 1048576.0:0} MB");
Console.WriteLine($"managed allocations {allocated / 1024.0:0} KB ({allocated / Math.Max(1, ticks):0} B per tick), gen0 GCs {GC.CollectionCount(0) - gcStart}");
Volatile.Write(ref running, false);
return 0;

internal static partial class Native
{
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completion, nint arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll")]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);
}
