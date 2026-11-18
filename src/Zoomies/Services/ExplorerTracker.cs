using System.Runtime.InteropServices;
using System.Windows.Threading;
using Zoomies.Core.Frecency;
using Zoomies.Interop;

namespace Zoomies.Services;

/// <summary>
/// Learns which folders the user works in by noticing what File Explorer shows. It only wakes up while an
/// Explorer window is in front (a foreground-change event starts a 2-second check), so it costs nothing otherwise.
/// </summary>
internal sealed class ExplorerTracker : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    private readonly FrecencyStore _store;
    private readonly DispatcherTimer _timer;
    private readonly WinEventProc _callback; // must stay referenced while the hook exists
    private readonly Dictionary<IntPtr, string> _lastSeen = [];
    private IntPtr _hook;

    public ExplorerTracker(FrecencyStore store)
    {
        _store = store;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Check(Native.GetForegroundWindow());
        _callback = OnForegroundChanged;
    }

    public bool Enabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value == Enabled) return;
            if (value)
            {
                _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }
            else
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
                _timer.Stop();
            }
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (ExplorerBridge.IsExplorerWindow(hwnd))
        {
            Check(hwnd);
            _timer.Start();
        }
        else _timer.Stop();
    }

    private void Check(IntPtr hwnd)
    {
        if (!ExplorerBridge.IsExplorerWindow(hwnd))
        {
            _timer.Stop();
            return;
        }
        string? folder = ExplorerBridge.GetActiveFolder(hwnd);
        if (folder is null) return;
        if (_lastSeen.TryGetValue(hwnd, out string? previous) && previous.Equals(folder, StringComparison.OrdinalIgnoreCase)) return;
        _lastSeen[hwnd] = folder;
        if (_lastSeen.Count > 64) _lastSeen.Clear(); // closed windows
        _store.Visit(folder);
    }

    public void Dispose() => Enabled = false;

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
