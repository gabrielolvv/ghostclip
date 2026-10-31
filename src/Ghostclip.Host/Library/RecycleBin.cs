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
        // pFrom is a list of paths, each null-terminated, ended by an extra null.
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + "\0");
        try
        {
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = from,
                // No progress window or prompt for a single clip; errors come back to the page.
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_NOERRORUI | FOF_SILENT,
            };
            var result = SHFileOperationW(ref op);
            if (op.fAnyOperationsAborted != 0) throw new IOException(L("Kept the clip.", "O clipe foi mantido."));
            if (result != 0) throw new IOException(L($"Windows couldn't delete the clip (error 0x{result:X}). Is it open in another program?",
                $"O Windows não conseguiu apagar o clipe (erro 0x{result:X}). Ele está aberto em outro programa?"));
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
