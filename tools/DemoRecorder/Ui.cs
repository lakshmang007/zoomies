using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace DemoRecorder;

/// <summary>Finds windows and controls (by their visible text, through UI Automation) and clicks them with a visible pointer.</summary>
internal static class Ui
{
    public static IntPtr WaitWindow(Func<IntPtr, bool> match, string what, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                if (IsWindowVisible(h) && match(h)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Window not found: {what}");
    }

    public static bool WaitGone(IntPtr hwnd, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>A control inside <paramref name="window"/> whose name matches, e.g. a button labelled "Organize now".</summary>
    public static AutomationElement WaitElement(IntPtr window, Func<string, bool> name, string what, int timeoutMs = 10000,
        ControlType? type = null)
    {
        // Fetch the properties in one round trip instead of one cross-process call per control.
        var cache = new CacheRequest();
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.IsEnabledProperty);
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using (cache.Activate())
                {
                    var root = AutomationElement.FromHandle(window);
                    foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                    {
                        var info = e.Cached;
                        if (info.IsOffscreen || !info.IsEnabled || info.BoundingRectangle.IsEmpty) continue;
                        if (type is not null && info.ControlType != type) continue;
                        if (name(info.Name ?? "")) return e;
                    }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            Thread.Sleep(150);
        }
        throw new TimeoutException($"Control not found: {what}");
    }

    public static AutomationElement WaitElement(IntPtr window, string exactName, ControlType? type = null, int timeoutMs = 10000) =>
        WaitElement(window, n => n == exactName, exactName, timeoutMs, type);

    public static void Click(AutomationElement element, int moveMs = 650)
    {
        var r = element.Cached.BoundingRectangle;
        Input.ClickAt((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), moveMs);
    }

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static string TitleOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static HashSet<IntPtr> TopLevelWindows()
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((h, _) => { set.Add(h); return true; }, IntPtr.Zero);
        return set;
    }

    public static void Place(IntPtr hwnd, int x, int y, int w, int h)
    {
        ShowWindow(hwnd, 9); // restore if maximized/minimized
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0004 | 0x0040); // NOZORDER | SHOWWINDOW
    }

    public static void Close(IntPtr hwnd) => PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE

    public static IntPtr Foreground => GetForegroundWindow();

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
