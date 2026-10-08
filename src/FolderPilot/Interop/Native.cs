using System.Runtime.InteropServices;
using System.Text;

namespace FolderPilot.Interop;

internal static class Native
{
    public const int WM_NULL = 0x0000;
    public const int WM_SETTEXT = 0x000C;
    public const int WM_GETTEXT = 0x000D;
    public const int WM_GETTEXTLENGTH = 0x000E;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_COPYDATA = 0x004A;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_COMMAND = 0x0111;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_APP = 0x8000;
    public const int VK_RETURN = 0x0D;
    public const int IDOK = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    // ── windows ──
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    public const int ASFW_ANY = -1;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);

    // ── hotkeys ──
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── menus ──
    public const uint MF_STRING = 0x0000, MF_GRAYED = 0x0001, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800, MF_DEFAULT = 0x1000;
    public const uint TPM_RIGHTBUTTON = 0x0002, TPM_RETURNCMD = 0x0100, TPM_BOTTOMALIGN = 0x0020, TPM_RIGHTALIGN = 0x0008;
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? text);
    [DllImport("user32.dll")] public static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);
    [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);

    // ── icons ──
    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    public const int SM_CXSMICON = 49;

    // ── shell ──
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] public static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint count, IntPtr[]? items, uint flags);
    [DllImport("shell32.dll")] public static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr ptr);

    // ── DWM (rounded corners, dark title bar) ──
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    // ── memory ──
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    // ── dark context menus (undocumented, used by many apps since Windows 10 1903; absent → no-op) ──
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();

    public static void FollowSystemThemeInMenus()
    {
        try
        {
            SetPreferredAppMode(1); // AllowDark: menus follow the Windows app theme
            FlushMenuThemes();
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static string GetText(IntPtr hWnd)
    {
        int length = (int)SendMessage(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        SendMessage(hWnd, WM_GETTEXT, sb.Capacity, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Brings a window to the front. Windows only lets the app the user is interacting with do this,
    /// so it briefly joins the input queue of the current foreground window.
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
        if (SetForegroundWindow(hWnd)) return;
        IntPtr fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        if (fgThread == me || fgThread == 0) { SetForegroundWindow(hWnd); return; }
        AttachThreadInput(me, fgThread, true);
        try { SetForegroundWindow(hWnd); }
        finally { AttachThreadInput(me, fgThread, false); }
    }
}
