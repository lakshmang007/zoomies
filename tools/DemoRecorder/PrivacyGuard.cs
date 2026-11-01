using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DemoRecorder;

/// <summary>
/// Keeps a cover over the navigation pane of guarded windows (File Explorer, Save dialogs), because that pane lists the
/// real user's OneDrive, pinned folders and devices. Several times a second it follows the window, re-shows the cover
/// if anything hid it, and puts it back on top if another window pushed it down. Runs on the UI thread.
/// </summary>
internal sealed class PrivacyGuard
{
    private sealed class Guarded
    {
        public required Rect Offset;   // pane position relative to the window's top-left corner
        public required Color Color;
        public bool WholeWindow;       // cover everything until the pane itself has been located
        public Window? Cover;
    }

    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private readonly Dictionary<IntPtr, Guarded> _guarded = [];
    private readonly DispatcherTimer _timer;
    private readonly WinEventProc _onShow;
    private readonly IntPtr _hook;

    public PrivacyGuard()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        // A Save/Open dialog opened by this process is covered the moment it appears, before the first frame
        // that could show the user's folders; Add() later narrows the cover to the navigation pane.
        _onShow = OnWindowShown;
        _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _onShow, (uint)Environment.ProcessId, 0, 0);
    }

    /// <summary>Starts covering <paramref name="paneOnScreen"/> (screen pixels) of <paramref name="window"/>.</summary>
    public void Add(IntPtr window, Rect paneOnScreen, Color color)
    {
        GetWindowRect(window, out var w);
        _guarded.TryGetValue(window, out var previous);
        _guarded[window] = new Guarded
        {
            Offset = new Rect(paneOnScreen.X - w.Left, paneOnScreen.Y - w.Top, paneOnScreen.Width, paneOnScreen.Height),
            Color = color,
        };
        Update();
        previous?.Cover?.Close(); // only after the new cover is up, so nothing shows in between
    }

    private void OnWindowShown(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || _guarded.ContainsKey(hwnd)) return;
        string cls = ClassOf(hwnd);
        // Save/Open dialogs of this process, and (once recording) any File Explorer window the script didn't open itself.
        bool cover = (hook == _hook && cls == "#32770") || (hook == _explorerHook && cls == "CabinetWClass");
        if (!cover) return;
        _guarded[hwnd] = new Guarded { Offset = Rect.Empty, Color = Color.FromRgb(0x20, 0x20, 0x20), WholeWindow = true };
        Update();
    }

    private IntPtr _explorerHook;

    /// <summary>From now on, a File Explorer window that appears unexpectedly is covered entirely until the script handles it.</summary>
    public void WatchNewExplorerWindows()
    {
        if (_explorerHook == IntPtr.Zero)
            _explorerHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _onShow, 0, 0, 0);
    }

    public void Remove(IntPtr window)
    {
        if (_guarded.Remove(window, out var g)) g.Cover?.Close();
    }

    public void Stop()
    {
        _timer.Stop();
        UnhookWinEvent(_hook);
        if (_explorerHook != IntPtr.Zero) UnhookWinEvent(_explorerHook);
        foreach (var g in _guarded.Values) g.Cover?.Close();
        _guarded.Clear();
    }

    private void Update()
    {
        foreach (var (hwnd, g) in _guarded.ToList())
        {
            if (!IsWindow(hwnd)) { Remove(hwnd); continue; }
            g.Cover ??= CreateCover(g);

            bool shown = IsWindowVisible(hwnd) && !IsIconic(hwnd);
            if (!shown)
            {
                if (g.Cover.IsVisible) g.Cover.Hide();
                continue;
            }
            GetWindowRect(hwnd, out var w);
            if (g.WholeWindow)
            {
                g.Cover.Width = Math.Max(1, w.Right - w.Left);
                g.Cover.Height = Math.Max(1, w.Bottom - w.Top);
            }
            double left = w.Left + (g.WholeWindow ? 0 : g.Offset.X), top = w.Top + (g.WholeWindow ? 0 : g.Offset.Y);
            if (g.Cover.Left != left || g.Cover.Top != top) { g.Cover.Left = left; g.Cover.Top = top; }
            if (!g.Cover.IsVisible) g.Cover.Show();

            IntPtr cover = new WindowInteropHelper(g.Cover).Handle;
            // Back on top if it lost WS_EX_TOPMOST, or if the guarded window is itself "always on top" and could rise above it.
            if ((GetWindowLong(cover, -20) & 0x8) == 0 || (GetWindowLong(hwnd, -20) & 0x8) != 0)
                SetWindowPos(cover, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
    }

    private static Window CreateCover(Guarded g)
    {
        var cover = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
            Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
            Width = g.WholeWindow ? 1 : g.Offset.Width, Height = g.WholeWindow ? 1 : g.Offset.Height,
            Background = new SolidColorBrush(g.Color),
        };
        cover.SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(cover).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x08000000 | 0x80); // click-through, never focused
        };
        // If anything ever closes the cover, the next tick makes a new one.
        cover.Closed += (_, _) => { if (g.Cover == cover) g.Cover = null; };
        return cover;
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(64);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
