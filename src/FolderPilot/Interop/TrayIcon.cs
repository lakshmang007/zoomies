using System.Runtime.InteropServices;

namespace FolderPilot.Interop;

/// <summary>Notification-area icon built directly on Shell_NotifyIcon (no WinForms dependency, which saves memory).</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int NIIF_USER = 0x4, NIIF_LARGE_ICON = 0x20;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const int NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401;

    private readonly MessageHost _host;
    private readonly IntPtr _icon;
    private string _tooltip;
    private bool _added;
    private long _lastClick;

    public TrayIcon(MessageHost host, string iconPath, string tooltip)
    {
        _host = host;
        _tooltip = tooltip;
        uint dpi = Native.GetDpiForSystem();
        int size = Native.GetSystemMetricsForDpi(Native.SM_CXSMICON, dpi);
        _icon = Native.LoadImage(IntPtr.Zero, iconPath, Native.IMAGE_ICON, size, size, Native.LR_LOADFROMFILE);
        host.TrayMessage += OnMessage;
        host.TaskbarRecreated += () => { _added = false; Show(); }; // Explorer restarted
    }

    public event Action? Click;
    public event Action<int, int>? MenuRequested;

    public void Show()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (!_added) return;
        var data = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>A Windows notification attributed to FolderPilot.</summary>
    public void Notify(string title, string text)
    {
        if (!_added) return;
        var data = Data(NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = _icon;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void OnMessage(int evt, int raw)
    {
        switch (evt)
        {
            case NIN_SELECT or NIN_KEYSELECT:
                // Some shells send both a select and a key-select; ignore the duplicate.
                if (Environment.TickCount64 - _lastClick < 400) return;
                _lastClick = Environment.TickCount64;
                Click?.Invoke();
                break;
            case Native.WM_CONTEXTMENU:
                Native.GetCursorPos(out var p);
                MenuRequested?.Invoke(p.X, p.Y);
                break;
        }
    }

    private NOTIFYICONDATAW Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _host.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = MessageHost.TrayCallbackMessage,
        hIcon = _icon,
        szTip = Truncate(_tooltip, 127),
        szInfo = "",
        szInfoTitle = "",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) Native.DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATAW data);
}
