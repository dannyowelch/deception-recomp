using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RecompOne.Runtime.Host.Window;

/// <summary>
/// Folder picker that opens in a chosen directory.
///
/// The shared dialog only passes a suggested folder. On Windows that suggestion
/// is dropped whenever any dialog in the process has been used before, so
/// Restore was opening at the last place the player browsed, often a drive
/// root, and the backups live under the hidden AppData directory. This picker
/// selects the folder itself, and shows hidden items so the path can be seen
/// if the player steps upward.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsFolderDialog
{
    internal enum Result { Chosen, Cancelled, Failed }

    const int CoInitApartmentThreaded = 0x2;
    const int CoInitDisableOle1Dde = 0x4;
    const int RpcEChangedMode = unchecked((int)0x80010106);
    const int HresultCancelled = unchecked((int)0x800704C7);

    const uint FosNoChangeDir = 0x8;
    const uint FosPickFolders = 0x20;
    const uint FosForceFileSystem = 0x40;
    const uint FosPathMustExist = 0x800;
    const uint FosDontAddToRecent = 0x2000000;
    const uint FosForceShowHidden = 0x10000000;
    const uint SigdnFileSysPath = 0x80058000;

    static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    internal static Result Pick(string start, string? title, out string? chosen)
    {
        chosen = null;
        IFileDialog? dialog = null;
        IShellItem? startItem = null;
        var uninit = false;
        try
        {
            var init = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded | CoInitDisableOle1Dde);
            uninit = init == 0 || init == 1;
            if (init < 0 && init != RpcEChangedMode) return Result.Failed;

            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(start));
            dialog = (IFileDialog)new FileOpenDialog();
            if (dialog.GetOptions(out var options) < 0) return Result.Failed;
            options |= FosNoChangeDir | FosPickFolders | FosForceFileSystem | FosPathMustExist
                | FosDontAddToRecent | FosForceShowHidden;
            // Options are applied first. Setting the folder earlier is cleared
            // when the picker is switched to folders.
            if (dialog.SetOptions(options) < 0) return Result.Failed;

            var iid = IidShellItem;
            if (SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out startItem) < 0
                || startItem == null)
                return Result.Failed;
            if (dialog.SetFolder(startItem) < 0) return Result.Failed;
            if (!string.IsNullOrWhiteSpace(title)) dialog.SetTitle(title);

            // The folder that will be selected when the dialog opens.
            if (!TryFolder(dialog, out var bound) || !SamePath(bound, full)) return Result.Failed;

            var shown = dialog.Show(GetForegroundWindow());
            if (shown == HresultCancelled) return Result.Cancelled;
            if (shown < 0) return Result.Failed;

            if (dialog.GetResult(out var result) < 0 || result == null) return Result.Failed;
            try
            {
                chosen = NameOf(result);
                return string.IsNullOrWhiteSpace(chosen) ? Result.Failed : Result.Chosen;
            }
            finally
            {
                Release(result);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] folder dialog: {e.Message}");
            return Result.Failed;
        }
        finally
        {
            Release(startItem);
            Release(dialog);
            if (uninit) CoUninitialize();
        }
    }

    static bool TryFolder(IFileDialog dialog, out string? path)
    {
        path = null;
        if (dialog.GetFolder(out var folder) < 0 || folder == null) return false;
        try
        {
            path = NameOf(folder);
            return !string.IsNullOrWhiteSpace(path);
        }
        finally
        {
            Release(folder);
        }
    }

    static string? NameOf(IShellItem item)
    {
        if (item.GetDisplayName(SigdnFileSysPath, out var ptr) < 0 || ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            var left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
            var right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static void Release(object? com)
    {
        try
        {
            if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] folder dialog release: {e.Message}");
        }
    }

    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    static extern void CoUninitialize();

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        [PreserveSig] int SetFileTypes(uint count, IntPtr filters);
        [PreserveSig] int SetFileTypeIndex(uint index);
        [PreserveSig] int GetFileTypeIndex(out uint index);
        [PreserveSig] int Advise(IntPtr events, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOptions(uint options);
        [PreserveSig] int GetOptions(out uint options);
        [PreserveSig] int SetDefaultFolder(IShellItem item);
        [PreserveSig] int SetFolder(IShellItem item);
        [PreserveSig] int GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem? item);
        [PreserveSig] int GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem? item);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetFileName(out IntPtr name);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        [PreserveSig] int GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem? item);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
        [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem? parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
}
