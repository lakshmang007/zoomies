using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using static DemoRecorder.Input;

namespace DemoRecorder;

/// <summary>
/// The demo, scene by scene: Find (Quick Jump in Explorer and in a Save dialog), Organize (preview, custom folder
/// name, undo) and Clean (unused files to the Recycle Bin, undo from History). About 90 seconds.
/// </summary>
internal sealed partial class DemoScript(Options options, Overlay overlay, Dispatcher ui)
{
    private const string HostTitle = "Zoomies.MessageHost.6c1f";
    private const string LauncherTitle = "Zoomies Quick Jump";
    private readonly HashSet<IntPtr> _windowsBefore = Ui.TopLevelWindows();
    private readonly List<IntPtr> _opened = [];
    private IntPtr _explorer;
    private IntPtr _hub;
    private PrivacyGuard? _guard;
    private string _hotkeyText = "Ctrl + Alt + Space";
    private ushort[] _hotkeyKeys = [Ctrl, Alt, Space];
    private bool _quiet; // no on-screen key hints

    public void Run()
    {
        // Fair warning before taking over the mouse and keyboard.
        for (int seconds = options.CountdownSeconds; seconds > 0; seconds--)
        {
            Caption($"Demo recording starts in {seconds}…", "Please don't touch the mouse or keyboard for about 2 minutes");
            Thread.Sleep(1000);
        }
        OnUi(overlay.HideCaption);

        bool realAppWasRunning = StopRunningZoomies();
        UseFreeHotkey();
        _guard = ui.Invoke(() => new PrivacyGuard());
        Shell("MinimizeAll");
        Thread.Sleep(800);
        ScreenRecorder? recorder = null;
        try
        {
            StartDemoApp();
            OpenExplorer(Path.Combine(options.Root, "Downloads"));
            // Hide the real desktop (wallpaper, widgets) and Explorer's list of the user's own folders.
            OnUi(() => overlay.ShowStage(options.ScreenWidth, options.ScreenHeight, _explorer, light: options.Theme == "sand"));
            MaskNavigationPane(_explorer);
            // Clicking into Explorer before recording makes an auto-hiding taskbar (with the real user's pinned apps) slide away.
            Focus(_explorer);
            SetCursorAway();
            Thread.Sleep(1500);

            OnUi(() => _guard!.WatchNewExplorerWindows());
            if (options.Script == "inserts")
            {
                RunInserts(); // records its own short clips
                return;
            }
            recorder = new ScreenRecorder(options.Ffmpeg, options.Output, options.ScreenWidth, options.ScreenHeight, options.Encoder);
            if (options.Script == "ad") RunAd();
            else RunDemo();
        }
        finally
        {
            recorder?.Stop();
            OnUi(() => { _guard?.Stop(); overlay.CloseAll(); });
            CleanUp(realAppWasRunning);
        }
    }

    // ───────────────────────────── the walkthrough demo ─────────────────────────────

    private void RunDemo()
    {
        OnUi(() => overlay.Card("Zoomies", "Find, organize and clean your files in one light app — with undo for everything.", "Windows  ·  Private  ·  Free"));
        Thread.Sleep(3800);
        OnUi(overlay.HideCard);
        Thread.Sleep(600);

        SceneFind();
        SceneSaveDialog();
        SceneOrganize();
        SceneClean();

        OnUi(() => overlay.Card("Zoomies", "Find, organize and clean your files in one light app — with undo for everything.",
            "Private and local   ·   Light   ·   Free"));
        Thread.Sleep(4800);
    }

    private void SceneFind()
    {
        Caption("Find any folder in a keystroke", $"Press {_hotkeyText} anywhere and type a few letters");
        Thread.Sleep(1600);
        Focus(_explorer);
        Jump("final");
        Thread.Sleep(1900);

        Caption("It learns the folders you use", "The jump happens right in the File Explorer window you're in");
        Jump("goa");
        Thread.Sleep(2300);
    }

    private void SceneSaveDialog()
    {
        Caption("Works in every app's Save and Open dialogs", "No more clicking through folders to save a file");
        SaveDialogJump("invoices", 2800);
    }

    /// <summary>Opens a Save dialog in the stand-in notes app and uses Quick Jump to land in another folder.</summary>
    private void SaveDialogJump(string query, int holdMs)
    {
        // A stand-in notes app, so the Save dialog opens inside the sample PC rather than the real user's last folder.
        IntPtr notes = ui.Invoke(() =>
        {
            // Opens on top so it is never hidden behind another window (this process can't take the foreground itself)…
            var window = new NotesWindow(Path.Combine(options.Root, "Documents")) { Left = 330, Top = 70, Width = 1100, Height = 760, Topmost = true };
            window.Show();
            return new System.Windows.Interop.WindowInteropHelper(window).Handle;
        });
        Thread.Sleep(1300);
        Focus(notes, 0.25);
        // …and stops being "always on top" once clicked, so its Save dialog stays below the privacy covers.
        ui.Invoke(() => { foreach (System.Windows.Window w in System.Windows.Application.Current.Windows) if (w is NotesWindow) w.Topmost = false; });
        ShowKeys("Ctrl + S");
        Chord(Ctrl, S);
        IntPtr dialog = Ui.WaitWindow(h => Ui.ClassOf(h) == "#32770"
            && Ui.TitleOf(h).StartsWith("Save", StringComparison.OrdinalIgnoreCase), "Save dialog", 8000);
        Ui.Place(dialog, 360, 150, 1000, 620);
        Thread.Sleep(500);
        MaskNavigationPane(dialog);
        Thread.Sleep(1300);
        Jump(query, expectedForeground: dialog);
        Thread.Sleep(holdMs);
        ShowKeys("Esc");
        Press(Escape);
        Ui.WaitGone(dialog);
        OnUi(() => _guard!.Remove(dialog));
        Thread.Sleep(500);
        Ui.Close(notes);
        Ui.WaitGone(notes);
        Thread.Sleep(600);
    }

    private void SceneOrganize()
    {
        Caption("Organize a messy folder in one go", "See exactly what will move — nothing happens until you say so");
        Focus(_explorer);
        Jump("downl");
        Thread.Sleep(1500);

        // Quick Jump knows which folder Explorer is showing, so "Organize this folder" is the first suggestion.
        Hotkey();
        Thread.Sleep(1500);
        ShowKeys("Enter");
        Press(Enter);
        _hub = Ui.WaitWindow(h => Ui.TitleOf(h) == "Zoomies"
            && Ui.ClassOf(h).StartsWith("HwndWrapper", StringComparison.Ordinal), "Zoomies window");
        Ui.Place(_hub, 950, 12, 955, 1015);
        var apply = Ui.WaitElement(_hub, "Organize now", ControlType.Button);
        Thread.Sleep(1800);

        Caption("Choose where each file type goes", "Search “stl”, then name the folder — it's remembered next time");
        var search = Ui.WaitElement(_hub, n => n.StartsWith("Find a file type", StringComparison.Ordinal), "search box", type: ControlType.Edit);
        Ui.Click(_hub, search);
        EnsureForeground(_hub, "Zoomies window");
        Type("stl", 150);
        Thread.Sleep(1100);
        var folderBox = Ui.WaitElement(_hub, "Folder for .stl files", ControlType.Edit);
        Ui.Click(_hub, folderBox);
        Chord(Ctrl, A);
        Type("3D Prints", 105);
        Thread.Sleep(1500);

        apply = Ui.WaitElement(_hub, "Organize now", ControlType.Button);
        Ui.Click(_hub, apply);
        Thread.Sleep(2900);

        Caption("Changed your mind? Undo it", "Everything Zoomies does can be undone");
        var undo = Ui.WaitElement(_hub, "Undo", ControlType.Button);
        Ui.Click(_hub, undo);
        Thread.Sleep(2500);
    }

    private void SceneClean()
    {
        Caption("Clean up files you haven't opened in months", "Old lecture videos and forgotten backups, found in seconds");
        Ui.Place(_hub, 280, 12, 1360, 1015);
        Thread.Sleep(700);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Clean up", ControlType.ListItem));
        Thread.Sleep(900);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Scan", ControlType.Button));
        var selectAll = Ui.WaitElement(_hub, "Select all", ControlType.Button, timeoutMs: 30000);
        Thread.Sleep(2000);
        Ui.Click(_hub, selectAll);
        Thread.Sleep(1000);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Move to Recycle Bin", ControlType.Button));
        IntPtr confirm = Ui.WaitWindow(h => Ui.ClassOf(h) == "#32770"
            && Ui.TitleOf(h) == "Move to Recycle Bin?", "confirmation");
        Thread.Sleep(1500);
        Ui.Click(confirm, Ui.WaitElement(confirm, "Yes", ControlType.Button));
        Thread.Sleep(2600);

        Caption("Safe by design", "Files go to the Recycle Bin, and History can put everything back");
        Ui.Click(_hub, Ui.WaitElement(_hub, "History", ControlType.ListItem));
        Thread.Sleep(1600);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Undo", ControlType.Button));
        Thread.Sleep(2800);
    }

    // ───────────────────────────── steps ─────────────────────────────

    private void Hotkey()
    {
        ShowKeys(_hotkeyText);
        Chord(_hotkeyKeys);
        IntPtr launcher = Ui.WaitWindow(h => Ui.TitleOf(h) == LauncherTitle, "Quick Jump", 4000);
        EnsureForeground(launcher, "Quick Jump");
        Thread.Sleep(450);
    }

    /// <summary>Summon Quick Jump, type, press Enter. Checks focus first so keystrokes can never land in another app.</summary>
    private void Jump(string text, IntPtr? expectedForeground = null)
    {
        if (expectedForeground is { } target) EnsureForeground(target, "target window");
        Hotkey();
        Type(text, 120);
        Thread.Sleep(1000);
        ShowKeys("Enter");
        Press(Enter);
        Thread.Sleep(300);
    }

    /// <param name="across">Where along the title bar to click (0 = left edge). Explorer's tabs and "+" button sit on the left, so it defaults to two thirds.</param>
    private void Focus(IntPtr window, double across = 2.0 / 3)
    {
        // A real click on the title bar, as a person would; refused if another window covers that spot.
        GetWindowRect(window, out var r);
        Ui.ClickOn(window, r.Left + (int)((r.Right - r.Left) * across), r.Top + 18, 550, Ui.TitleOf(window));
        Thread.Sleep(300);
        EnsureForeground(window, Ui.TitleOf(window));
    }

    private static void EnsureForeground(IntPtr window, string what)
    {
        for (int i = 0; i < 20; i++)
        {
            IntPtr fg = Ui.Foreground;
            if (fg == window || GetAncestor(fg, 3) == window) return; // GA_ROOTOWNER
            Thread.Sleep(100);
        }
        throw new InvalidOperationException($"Stopped: '{what}' is not in front, so typing could reach another app.");
    }

    /// <summary>
    /// Another app may own Zoomies' default shortcut (the Claude desktop app uses Ctrl+Alt+Space).
    /// Pick the first one that is actually free and give it to the demo copy of Zoomies.
    /// </summary>
    private void UseFreeHotkey()
    {
        (string Text, string Setting, uint Mods, uint Vk, ushort[] Keys)[] candidates =
        [
            ("Ctrl + Alt + Space", "Ctrl+Alt+Space", 0x2 | 0x1, 0x20, [Ctrl, Alt, Space]),
            ("Ctrl + Shift + Space", "Ctrl+Shift+Space", 0x2 | 0x4, 0x20, [Ctrl, Shift, Space]),
            ("Alt + Shift + F", "Alt+Shift+F", 0x1 | 0x4, 0x46, [Alt, Shift, F]),
        ];
        foreach (var c in candidates)
        {
            if (!RegisterHotKey(IntPtr.Zero, 0x7A11, c.Mods | 0x4000, c.Vk)) continue;
            UnregisterHotKey(IntPtr.Zero, 0x7A11);
            (_hotkeyText, _hotkeyKeys) = (c.Text, c.Keys);
            DemoData.SetHotkey(options.Root, c.Setting, options.Theme);
            return;
        }
        throw new InvalidOperationException("No free keyboard shortcut for Quick Jump.");
    }

    /// <summary>Covers a window's navigation pane (which lists the real user's folders, OneDrive and devices) with its own background colour.</summary>
    private void MaskNavigationPane(IntPtr window)
    {
        // Explorer windows and file dialogs have exactly one tree control: the navigation pane.
        AutomationElement tree;
        try { tree = Ui.WaitElement(window, _ => true, "navigation pane", 6000, ControlType.Tree); }
        catch (TimeoutException) { throw new InvalidOperationException("Stopped: could not find the navigation pane to hide it."); }
        var r = tree.Cached.BoundingRectangle;
        var color = PixelAt((int)r.Left + 4, (int)r.Bottom - 4);
        OnUi(() => _guard!.Add(window, new System.Windows.Rect(r.Left, r.Top, r.Width, r.Height), color));
    }

    private static System.Windows.Media.Color PixelAt(int x, int y)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        uint c = GetPixel(dc, x, y);
        ReleaseDC(IntPtr.Zero, dc);
        return System.Windows.Media.Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
    }

    private void Caption(string title, string subtitle) => OnUi(() => overlay.Caption(title, subtitle));
    private void ShowKeys(string keys)
    {
        if (!_quiet) OnUi(() => overlay.Keys(keys));
    }
    private void OnUi(Action action) => ui.Invoke(action);

    private bool IsNew(IntPtr h) => !_windowsBefore.Contains(h);

    private void SetCursorAway() => MoveTo(options.ScreenWidth - 260, options.ScreenHeight - 300, 300);

    // ───────────────────────────── setup & cleanup ─────────────────────────────

    private void StartDemoApp()
    {
        var start = new ProcessStartInfo(options.App, "--background") { UseShellExecute = false };
        start.Environment["ZOOMIES_DEMO_ROOT"] = options.Root;
        Process.Start(start)?.Dispose();
        var sw = Stopwatch.StartNew();
        while (FindWindow(null, HostTitle) == IntPtr.Zero)
        {
            if (sw.ElapsedMilliseconds > 15000) throw new TimeoutException("Zoomies (demo) did not start.");
            Thread.Sleep(200);
        }
        Thread.Sleep(3500); // first index build of the small demo tree
    }

    private void OpenExplorer(string folder)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false })?.Dispose();
        string name = Path.GetFileName(folder);
        _explorer = Ui.WaitWindow(h => IsNew(h) && Ui.ClassOf(h) == "CabinetWClass"
            && Ui.TitleOf(h).StartsWith(name, StringComparison.OrdinalIgnoreCase), "File Explorer", 10000);
        _opened.Add(_explorer);
        Ui.Place(_explorer, 15, 12, 925, 1015);
        Thread.Sleep(1500);
    }

    /// <summary>The user's own Zoomies holds the shortcut; close it for the demo and reopen it afterwards.</summary>
    private bool StopRunningZoomies()
    {
        if (FindWindow(null, HostTitle) == IntPtr.Zero) return false;
        Process.Start(new ProcessStartInfo(options.App, "--quit") { UseShellExecute = false })?.WaitForExit(10000);
        var sw = Stopwatch.StartNew();
        while (FindWindow(null, HostTitle) != IntPtr.Zero && sw.ElapsedMilliseconds < 10000) Thread.Sleep(200);
        return true;
    }

    private void CleanUp(bool restartRealApp)
    {
        try
        {
            if (_hub != IntPtr.Zero) Ui.Close(_hub);
            foreach (IntPtr w in _opened) Ui.Close(w);
            var quit = new ProcessStartInfo(options.App, "--quit") { UseShellExecute = false };
            quit.Environment["ZOOMIES_DEMO_ROOT"] = options.Root;
            Process.Start(quit)?.WaitForExit(10000);
            Thread.Sleep(1500);
            Shell("UndoMinimizeALL");
            if (restartRealApp) Process.Start(new ProcessStartInfo(options.App, "--background") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) { Console.Error.WriteLine("Cleanup: " + ex.Message); }
    }

    private static void Shell(string method)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(System.Type.GetTypeFromProgID("Shell.Application")!)!;
            if (method == "MinimizeAll") shell.MinimizeAll();
            else shell.UndoMinimizeALL();
        }
        catch (COMException) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
}
