using System.IO;
using System.Runtime.InteropServices;

namespace AssettoServer.RaceControl.Infrastructure;

/// <summary>Moves folders to the Windows Recycle Bin so a cleanup can be undone.</summary>
internal static class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    // Warn instead of silently deleting for good when an item is too big for the Recycle Bin.
    private const ushort FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct operation);

    /// <summary>Returns true when the folder is gone afterwards.</summary>
    public static bool MoveDirectory(string path, IntPtr owner)
    {
        var operation = new ShFileOpStruct
        {
            Hwnd = owner,
            Func = FoDelete,
            From = Path.GetFullPath(path) + "\0\0", // the list is double-null terminated
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning,
        };
        int result = SHFileOperation(ref operation);
        return result == 0 && !operation.AnyOperationsAborted && !Directory.Exists(path);
    }
}
