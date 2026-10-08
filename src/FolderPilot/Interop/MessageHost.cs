using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FolderPilot.Interop;

/// <summary>
/// An invisible window that receives the global hotkey, tray icon clicks, theme changes and
/// commands from a second copy of FolderPilot (e.g. from the Explorer right-click menu).
/// </summary>
internal sealed class MessageHost : IDisposable
{
    /// <summary>Window title other FolderPilot processes look for.</summary>
    public const string WindowTitle = "FolderPilot.MessageHost.6c1f";

    private readonly HwndSource _source;
    private readonly int _taskbarCreated;

    public MessageHost()
    {
        // A real (hidden) top-level window rather than a message-only one: the tray menu needs a window that can take focus.
        _source = new HwndSource(new HwndSourceParameters(WindowTitle) { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    public IntPtr Handle => _source.Handle;

    public event Action<int>? Hotkey;
    public event Action<int, int>? TrayMessage; // (event, raw lParam)
    public event Action? TaskbarRecreated;
    public event Action<string[]>? CommandReceived;
    public event Action? ThemeChanged;

    public const int TrayCallbackMessage = Native.WM_APP + 1;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_HOTKEY:
                Hotkey?.Invoke((int)wParam);
                handled = true;
                break;
            case TrayCallbackMessage:
                TrayMessage?.Invoke((int)lParam & 0xFFFF, (int)lParam);
                handled = true;
                break;
            case Native.WM_COPYDATA:
                var data = Marshal.PtrToStructure<Native.COPYDATASTRUCT>(lParam);
                if (data.dwData == CopyDataId && data.cbData > 0)
                {
                    string text = Marshal.PtrToStringUni(data.lpData, data.cbData / 2);
                    CommandReceived?.Invoke(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                handled = true;
                return 1;
            case Native.WM_SETTINGCHANGE:
                if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") ThemeChanged?.Invoke();
                break;
            default:
                if (msg == _taskbarCreated && _taskbarCreated != 0) TaskbarRecreated?.Invoke();
                break;
        }
        return IntPtr.Zero;
    }

    private static readonly IntPtr CopyDataId = 0x46504C54; // "FPLT"

    /// <summary>Hands command-line arguments to the copy of FolderPilot that is already running.</summary>
    public static bool SendToRunningInstance(string[] args)
    {
        IntPtr target = Native.FindWindow(null, WindowTitle);
        if (target == IntPtr.Zero) return false;
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        string text = args.Length == 0 ? "--show" : string.Join('\n', args);
        IntPtr buffer = Marshal.StringToHGlobalUni(text);
        try
        {
            var cds = new Native.COPYDATASTRUCT { dwData = CopyDataId, cbData = text.Length * 2, lpData = buffer };
            Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, ref cds);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
