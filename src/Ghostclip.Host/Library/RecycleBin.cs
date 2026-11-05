using System.Runtime.InteropServices;

namespace Ghostclip.Host.Library;

/// <summary>
/// Sends a file to the Recycle Bin through the shell. Where the drive has no
/// Recycle Bin (a network share, many USB drives), Windows asks before deleting
/// it for good instead of doing so silently.
/// </summary>
internal static partial class RecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>Throws <see cref="IOException"/> if the file wasn't deleted, including when the user chose to keep it.</summary>
    public static void Send(string path, nint owner)
    {
        var (result, aborted) = Run([path], owner);
        if (aborted) throw new IOException(L("Kept the clip.", "O clipe foi mantido.", "Se conservó el clip."));
        if (result != 0) throw new IOException(L($"Windows couldn't delete the clip (error 0x{result:X}). Is it open in another program?",
            $"O Windows não conseguiu apagar o clipe (erro 0x{result:X}). Ele está aberto em outro programa?", $"Windows no pudo eliminar el clip (error 0x{result:X}). ¿Está abierto en otro programa?"));
    }

    /// <summary>
    /// For what runs on its own, on its own STA thread as the shell wants. Normally silent. But Windows can't always
    /// recycle (a clip bigger than the bin, or a drive set not to use it), and it would then destroy the file: so it
    /// keeps its warning, which is a window. Not for use during a game. Returns the ones still there, and whether the
    /// person said to keep them.
    /// </summary>
    public static (IReadOnlyList<string> Kept, bool Declined) SendFromBackground(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return ([], false);
        var declined = false;
        var thread = new Thread(() =>
        {
            var (result, aborted) = Run(paths, 0);
            declined = aborted;
            if (result == 0 || aborted) return;
            // One in use can stop the rest, which get a try of their own.
            foreach (var path in paths.Where(File.Exists))
            {
                if (Run([path], 0).Aborted)
                {
                    declined = true;
                    return;
                }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return ([.. paths.Where(File.Exists)], declined);
    }

    /// <summary>
    /// Whether to try at all: a network share or a USB stick has no Recycle Bin, or may not, and asking about every
    /// clip there would be no use. A fixed drive usually has one, but see <see cref="SendFromBackground"/>.
    /// </summary>
    public static bool HasOne(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return !string.IsNullOrEmpty(root) && !root.StartsWith(@"\", StringComparison.Ordinal) && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Several in one go, so a drive with no Recycle Bin asks once and not per file. Returns the ones still there:
    /// kept, or in use. One in use can stop the rest, which get a try of their own, unless the user said to keep them.
    /// </summary>
    public static IReadOnlyList<string> SendAll(IReadOnlyList<string> paths, nint owner)
    {
        if (paths.Count == 0) return [];
        var (result, aborted) = Run(paths, owner);
        if (result != 0 && !aborted)
        {
            foreach (var path in paths.Where(File.Exists)) Run([path], owner);
        }
        return [.. paths.Where(File.Exists)];
    }

    private static (int Result, bool Aborted) Run(IReadOnlyList<string> paths, nint owner)
    {
        // pFrom is a list of paths, each null-terminated, ended by an extra null.
        var from = Marshal.StringToHGlobalUni(string.Concat(paths.Select(p => Path.GetFullPath(p) + "\0")));
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = from,
                // No progress window or prompt; errors come back to the page.
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NOERRORUI | FOF_SILENT,
            };
            var result = SHFileOperationW(ref op);
            return (result, op.fAnyOperationsAborted != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public nint pFrom;
        public nint pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public nint lpszProgressTitle;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHFileOperationW(ref SHFILEOPSTRUCTW operation);
}
