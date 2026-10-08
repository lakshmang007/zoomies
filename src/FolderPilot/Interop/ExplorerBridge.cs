using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FolderPilot.Interop;

/// <summary>
/// Talks to File Explorer through the shell's own automation interfaces: finds the folder shown in the active
/// tab, navigates a window in place, and drives the standard Open/Save dialog every Windows app uses.
/// Must be called on an STA thread (the UI thread).
/// </summary>
internal static class ExplorerBridge
{
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static object? _shell;

    public static bool IsExplorerWindow(IntPtr hwnd) => Native.GetClassName(hwnd) is "CabinetWClass" or "ExploreWClass";

    /// <summary>A standard Open/Save dialog (any app) with a file-name box we can type into.</summary>
    public static bool IsFileDialog(IntPtr hwnd) => Native.GetClassName(hwnd) == "#32770" && FindFileNameBox(hwnd) != IntPtr.Zero;

    /// <summary>The folder shown in the window's active tab, or null for non-folder places like "This PC".</summary>
    public static string? GetActiveFolder(IntPtr explorerWindow)
    {
        object? browser = ActiveBrowser(explorerWindow);
        return browser is null ? null : FolderOf(browser);
    }

    /// <summary>Every folder open in File Explorer (one entry per tab), front-most window first.</summary>
    public static List<string> GetOpenFolders()
    {
        var result = new List<string>();
        foreach (var (browser, _) in Browsers())
        {
            string? path = FolderOf((object)browser);
            if (path is not null && !result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
        }
        return result;
    }

    /// <summary>Shows <paramref name="folder"/> in the active tab of an existing Explorer window.</summary>
    public static bool Navigate(IntPtr explorerWindow, string folder)
    {
        dynamic? browser = ActiveBrowser(explorerWindow);
        if (browser is null) return false;
        try
        {
            browser.Navigate2(folder);
            Native.ForceForeground(explorerWindow);
            return true;
        }
        catch (COMException) { return false; }
    }

    /// <summary>
    /// Types the folder into the dialog's file-name box and presses Open — the dialog's own way to change folder.
    /// Whatever file name the user had typed is put back afterwards.
    /// </summary>
    public static bool NavigateFileDialog(IntPtr dialog, string folder)
    {
        IntPtr box = FindFileNameBox(dialog);
        if (box == IntPtr.Zero) return false;
        string original = Native.GetText(box);
        Native.SendMessage(box, Native.WM_SETTEXT, IntPtr.Zero, folder);
        Native.ForceForeground(dialog);
        Native.PostMessage(dialog, Native.WM_COMMAND, Native.IDOK, Native.GetDlgItem(dialog, Native.IDOK));

        if (original.Length > 0 && !original.Contains('\\'))
        {
            var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            restore.Tick += (_, _) =>
            {
                restore.Stop();
                if (Native.IsWindow(box)) Native.SendMessage(box, Native.WM_SETTEXT, IntPtr.Zero, original);
            };
            restore.Start();
        }
        return true;
    }

    public static void OpenInNewWindow(string folder) =>
        // Through the shell rather than explorer.exe directly, so a replacement file manager (if the user has one) is respected.
        Start(new ProcessStartInfo(folder) { UseShellExecute = true, Verb = "open" });

    public static void OpenFile(string file) => Start(new ProcessStartInfo(file) { UseShellExecute = true });

    /// <summary>Opens the containing folder with the item selected, reusing a window that already shows it.</summary>
    public static void Reveal(string path)
    {
        if (Native.SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
        {
            Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            return;
        }
        try { Native.SHOpenFolderAndSelectItems(pidl, 0, null, 0); }
        finally { Native.CoTaskMemFree(pidl); }
    }

    private static void Start(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    // ───────────────────────────── internals ─────────────────────────────

    private static dynamic Shell()
    {
        _shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        return _shell;
    }

    /// <summary>All Explorer browser objects (one per tab) with their top-level window.</summary>
    private static List<(dynamic Browser, IntPtr Window)> Browsers()
    {
        var list = new List<(dynamic, IntPtr)>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                dynamic windows = Shell().Windows();
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic? w = windows.Item(i);
                        if (w is null) continue;
                        var hwnd = new IntPtr(Convert.ToInt64(w.HWND));
                        if (IsExplorerWindow(hwnd)) list.Add((w, hwnd));
                    }
                    catch (COMException) { }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                }
                return list;
            }
            catch (COMException)
            {
                // Explorer restarted since we last asked; get a fresh connection once.
                _shell = null;
                list.Clear();
            }
        }
        return list;
    }

    /// <summary>
    /// On Windows 11 one window holds several tabs, each its own browser object. The active tab's
    /// "ShellTabWindowClass" child is first in z-order, and each browser reports its tab window.
    /// </summary>
    private static dynamic? ActiveBrowser(IntPtr explorerWindow)
    {
        var mine = Browsers().Where(b => b.Window == explorerWindow).ToList();
        if (mine.Count == 0) return null;
        if (mine.Count == 1) return mine[0].Browser;
        IntPtr activeTab = Native.FindWindowEx(explorerWindow, IntPtr.Zero, "ShellTabWindowClass", null);
        foreach (var (browser, _) in mine)
            if (activeTab != IntPtr.Zero && TabWindowOf(browser) == activeTab) return browser;
        return mine[0].Browser;
    }

    private static IntPtr TabWindowOf(object browser)
    {
        if (browser is not IComServiceProvider provider) return IntPtr.Zero;
        Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
        if (provider.QueryService(ref sid, ref iid, out IntPtr ptr) != 0 || ptr == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(ptr);
            return shellBrowser.GetWindow(out IntPtr hwnd) == 0 ? hwnd : IntPtr.Zero;
        }
        finally { Marshal.Release(ptr); }
    }

    private static string? FolderOf(object browserObject)
    {
        dynamic browser = browserObject;
        try
        {
            string? path = browser.Document?.Folder?.Self?.Path;
            // Places like "This PC" or "Home" report a "::{GUID}" path; only real folders count.
            return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : null;
        }
        catch (COMException) { return null; }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return null; }
    }

    /// <summary>Finds the "File name" box: classic id 1148 (cmb13), old-style id 1152 (edt1), or the first visible combo-box edit.</summary>
    private static IntPtr FindFileNameBox(IntPtr dialog)
    {
        IntPtr combo = Native.GetDlgItem(dialog, 0x47C);
        if (combo != IntPtr.Zero)
        {
            IntPtr edit = Native.FindWindowEx(combo, IntPtr.Zero, "Edit", null);
            if (edit == IntPtr.Zero)
            {
                IntPtr inner = Native.FindWindowEx(combo, IntPtr.Zero, "ComboBox", null);
                if (inner != IntPtr.Zero) edit = Native.FindWindowEx(inner, IntPtr.Zero, "Edit", null);
            }
            if (edit != IntPtr.Zero) return edit;
        }

        IntPtr edt1 = Native.GetDlgItem(dialog, 0x480);
        if (edt1 != IntPtr.Zero && Native.GetClassName(edt1) == "Edit") return edt1;

        IntPtr found = IntPtr.Zero;
        Native.EnumChildWindows(dialog, (h, _) =>
        {
            if (Native.GetClassName(h) == "Edit" && Native.IsWindowVisible(h)
                && Native.GetClassName(Native.GetAncestor(h, 1)) == "ComboBox")
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    /// <summary>Only the first method (IOleWindow.GetWindow) is used, so the rest of the vtable is not declared.</summary>
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
    }
}
