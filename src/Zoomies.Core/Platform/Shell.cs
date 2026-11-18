using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Zoomies.Core.Platform;

public static class Shell
{
    /// <summary>Tells open Explorer windows that a folder's contents changed so they refresh right away.</summary>
    public static void NotifyFolderChanged(string folder)
    {
        try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, folder, IntPtr.Zero); }
        catch (Exception) { }
    }

    /// <summary>
    /// Whether NTFS records when files were last opened. When it is off (a Windows setting),
    /// "last used" falls back to the last time the file was changed.
    /// </summary>
    public static bool IsLastAccessTracked()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            object? value = key?.GetValue("NtfsDisableLastAccessUpdate");
            if (value is int i) return (i & 1) == 0;
            return true;
        }
        catch (Exception) { return false; }
    }

    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSHNOWAIT = 0x3000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);
}
