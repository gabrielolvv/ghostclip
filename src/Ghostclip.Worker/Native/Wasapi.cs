using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Ghostclip.Worker.Native;

// Minimal WASAPI surface for loopback capture. Source-generated COM interop
// (no built-in COM marshalling), so it works under trimming and Native AOT.
// Methods are declared in vtable order; only the ones used are called.

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out nint devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig] int Activate(in Guid iid, uint clsCtx, nint activationParams, out nint instance);
    [PreserveSig] int OpenPropertyStore(uint access, out nint properties);
    [PreserveSig] int GetId(out string? id);
    [PreserveSig] int GetState(out uint state);
}

[GeneratedComInterface]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
internal partial interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, in WaveFormatExtensible format, nint sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
    [PreserveSig] int GetMixFormat(out nint format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(nint handle);
    [PreserveSig] int GetService(in Guid iid, out nint service);
}

[GeneratedComInterface]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
internal partial interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[GeneratedComInterface]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
internal partial interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(nint operation);
}

[GeneratedComInterface]
[Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
internal partial interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig] int GetActivateResult(out int activateResult, out nint activatedInterface);
}

/// <summary>Marks the completion handler as free-threaded, which ActivateAudioInterfaceAsync requires.</summary>
[GeneratedComInterface]
[Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
internal partial interface IAgileObject
{
}

[GeneratedComClass]
internal sealed partial class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    private readonly ManualResetEventSlim _done = new();
    private int _hr;
    private nint _client;

    public int ActivateCompleted(nint operation)
    {
        // Borrowed pointer: wrap it without taking ownership.
        var op = (IActivateAudioInterfaceAsyncOperation)Wasapi.ComWrappers.GetOrCreateObjectForComInstance(operation, CreateObjectFlags.None);
        var hr = op.GetActivateResult(out var activateResult, out var client);
        _hr = hr < 0 ? hr : activateResult;
        _client = client;
        _done.Set();
        return 0;
    }

    /// <summary>Waits for the activation and returns the IAudioClient pointer (owned by the caller).</summary>
    public nint Wait(TimeSpan timeout)
    {
        if (!_done.Wait(timeout)) throw new TimeoutException("Process loopback activation timed out.");
        Wasapi.Check(_hr);
        return _client;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatExtensible
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
    public ushort ValidBitsPerSample;
    public uint ChannelMask;
    public Guid SubFormat;

    private static readonly Guid IeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>32-bit float stereo: the format FFmpeg reads from the pipe (f32le).</summary>
    public static WaveFormatExtensible FloatStereo(int sampleRate) => new()
    {
        FormatTag = 0xFFFE, // WAVE_FORMAT_EXTENSIBLE
        Channels = 2,
        SamplesPerSec = (uint)sampleRate,
        BitsPerSample = 32,
        BlockAlign = 8,
        AvgBytesPerSec = (uint)sampleRate * 8,
        ExtraSize = 22,
        ValidBitsPerSample = 32,
        ChannelMask = 0x3, // front left | front right
        SubFormat = IeeeFloat,
    };
}

internal static partial class Wasapi
{
    public const int AUDCLNT_SHAREMODE_SHARED = 0;
    public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    public const int eRender = 0;
    public const int eCapture = 1;
    public const int eConsole = 0;
    private const uint CLSCTX_ALL = 0x17;
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly StrategyBasedComWrappers ComWrappers = new();

    /// <summary>
    /// An IAudioClient that captures only what one process tree plays, or with
    /// <paramref name="exclude"/> everything but it (Windows 10 2004+). Nothing is
    /// injected into the game: Windows routes the audio itself.
    /// </summary>
    public static unsafe IAudioClient ActivateProcessLoopback(uint processId, bool exclude = false)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { PROCESS_LOOPBACK, { pid, INCLUDE_ or EXCLUDE_TARGET_PROCESS_TREE } }
        var parameters = stackalloc int[3];
        parameters[0] = 1;
        parameters[1] = (int)processId;
        parameters[2] = exclude ? 1 : 0;
        // PROPVARIANT holding a VT_BLOB that points at the parameters.
        var variant = stackalloc byte[24];
        new Span<byte>(variant, 24).Clear();
        *(ushort*)variant = 65; // VT_BLOB
        *(uint*)(variant + 8) = 12;
        *(nint*)(variant + 16) = (nint)parameters;

        var handler = new ActivationHandler();
        var unknown = ComWrappers.GetOrCreateComInterfaceForObject(handler, CreateComInterfaceFlags.None);
        try
        {
            var iid = typeof(IActivateAudioInterfaceCompletionHandler).GUID;
            Check(Marshal.QueryInterface(unknown, in iid, out var handlerPtr));
            try
            {
                var clientIid = typeof(IAudioClient).GUID;
                Check(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", clientIid, (nint)variant, handlerPtr, out var operation));
                try
                {
                    return Wrap<IAudioClient>(handler.Wait(TimeSpan.FromSeconds(3)));
                }
                finally
                {
                    if (operation != 0) Marshal.Release(operation);
                }
            }
            finally
            {
                Marshal.Release(handlerPtr);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public static void InitializeThread() => CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */);

    public static IMMDeviceEnumerator CreateEnumerator()
    {
        var iid = typeof(IMMDeviceEnumerator).GUID;
        Check(CoCreateInstance(CLSID_MMDeviceEnumerator, 0, CLSCTX_ALL, iid, out var ptr));
        return Wrap<IMMDeviceEnumerator>(ptr);
    }

    public static T Activate<T>(IMMDevice device) where T : class
    {
        var iid = typeof(T).GUID;
        Check(device.Activate(iid, CLSCTX_ALL, 0, out var ptr));
        return Wrap<T>(ptr);
    }

    public static T GetService<T>(IAudioClient client) where T : class
    {
        var iid = typeof(T).GUID;
        Check(client.GetService(iid, out var ptr));
        return Wrap<T>(ptr);
    }

    /// <summary>Releases the COM object now instead of waiting for the GC.</summary>
    public static void Release(object? com)
    {
        if (com is ComObject c) c.FinalRelease();
    }

    public static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static T Wrap<T>(nint ptr) where T : class
    {
        try
        {
            return (T)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsContext, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("Mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ActivateAudioInterfaceAsync(string deviceInterfacePath, in Guid iid, nint activationParams, nint completionHandler, out nint operation);
}
