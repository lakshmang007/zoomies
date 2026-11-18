using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Zoomies.Core;
using Zoomies.Core.Indexing;
using Zoomies.Interop;
using Zoomies.Localization;
using Zoomies.Services;
using Zoomies.Themes;

namespace Zoomies.UI;

public sealed class ResultItem
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public string Glyph { get; init; } = Display.FolderGlyph;
    public string Badge { get; init; } = "";
    public string? Path { get; init; }
    public bool IsDirectory { get; init; }
    public Action? Run { get; init; }
    public double Score { get; init; }

    public override string ToString() => Subtitle.Length == 0 ? Title : $"{Title}, {Subtitle}";
}

/// <summary>
/// The Quick Jump box. It remembers which window was in front when it was summoned, so a chosen folder opens
/// right there: in that File Explorer tab, or in that app's Open/Save dialog.
/// </summary>
public partial class LauncherWindow : Window
{
    private enum Target { None, Explorer, FileDialog }

    private readonly App _app = App.Current;
    private IntPtr _previous;
    private Target _target;
    private string? _targetFolder;
    private CancellationTokenSource? _searchCts;
    private int _version;
    private readonly DispatcherTimer _footerReset;

    public LauncherWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => { if (IsVisible) HideLauncher(restoreFocus: false); };
        SourceInitialized += (_, _) => ThemeManager.StyleWindow(this);
        _footerReset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _footerReset.Tick += (_, _) => { _footerReset.Stop(); Footer.Text = Loc.T("Launcher.Hints"); };
    }

    public void ShowFor(IntPtr previous)
    {
        MemoryTrimmer.Cancel();
        IntPtr me = new WindowInteropHelper(this).Handle;
        _previous = previous != me ? previous : _previous;
        DetectTarget();

        Query.Text = "";
        ShowDefaultResults();
        Show();
        PositionOnActiveMonitor();
        Activate();
        Native.ForceForeground(new WindowInteropHelper(this).Handle);
        Query.Focus();
        Keyboard.Focus(Query);
    }

#if DEBUG
    /// <summary>For screenshots: shows results for a query without stealing focus.</summary>
    internal async Task PrepareSnapshot(string query)
    {
        Query.Text = query;
        await Task.Delay(400);
    }
#endif

    private void DetectTarget()
    {
        _target = Target.None;
        _targetFolder = null;
        if (ExplorerBridge.IsExplorerWindow(_previous))
        {
            _target = Target.Explorer;
            _targetFolder = ExplorerBridge.GetActiveFolder(_previous);
            ContextText.Text = _targetFolder is null
                ? Loc.T("Launcher.InExplorer")
                : $"{Loc.T("Launcher.InExplorer")}  ·  {PathUtil.DisplayName(_targetFolder)}";
        }
        else if (ExplorerBridge.IsFileDialog(_previous))
        {
            _target = Target.FileDialog;
            ContextText.Text = Loc.T("Launcher.InDialog");
        }
        ContextChip.Visibility = _target == Target.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HideLauncher(bool restoreFocus)
    {
        _searchCts?.Cancel();
        Hide();
        Results.ItemsSource = null;
        if (restoreFocus && Native.IsWindow(_previous)) Native.SetForegroundWindow(_previous);
        MemoryTrimmer.TrimSoon(TimeSpan.FromSeconds(60));
    }

    // ───────────────────────────── results ─────────────────────────────

    private void ShowDefaultResults()
    {
        var items = new List<ResultItem>();
        items.AddRange(ContextActions());
        foreach (var (path, _) in _app.Frecency.Top(9))
        {
            if (!Directory.Exists(path)) continue;
            items.Add(FolderItem(path, Loc.T("Launcher.Frequent")));
        }
        SetResults(items, Loc.T("Launcher.EmptyHint"));
    }

    private async void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        int version = ++_version;
        _searchCts?.Cancel();
        string query = Query.Text.Trim();
        if (query.Length == 0)
        {
            ShowDefaultResults();
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        await Task.Delay(25); // let fast typing settle
        if (version != _version) return;
        List<ResultItem> items;
        try { items = await Task.Run(() => BuildResults(query, cts.Token), cts.Token); }
        catch (OperationCanceledException) { return; }
        if (version != _version) return;

        string empty = _app.Index.State == IndexState.Building
            ? Loc.F("Launcher.Indexing", _app.Index.ItemsSoFar)
            : Loc.T("Launcher.NoResults");
        SetResults(items, empty);
    }

    private void SetResults(List<ResultItem> items, string emptyMessage)
    {
        Results.ItemsSource = items;
        Results.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = emptyMessage;
        EmptyText.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (items.Count > 0)
        {
            Results.SelectedIndex = 0;
            Results.ScrollIntoView(items[0]);
        }
    }

    /// <summary>Runs off the UI thread.</summary>
    private List<ResultItem> BuildResults(string query, CancellationToken ct)
    {
        if (query.StartsWith('>')) return Actions(query[1..].Trim());
        if (LooksLikePath(query)) return BrowsePath(query, ct);

        var byPath = new Dictionary<string, ResultItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, score) in _app.Frecency.Match(query, 8))
        {
            if (Directory.Exists(path)) byPath[path] = FolderItem(path, Loc.T("Launcher.Frequent"), score);
        }
        foreach (var hit in _app.Index.Search(query, 30, SearchScope.All, _app.Frecency.SearchBoost, ct))
        {
            if (byPath.TryGetValue(hit.Path, out var existing) && existing.Score >= hit.Score) continue;
            byPath[hit.Path] = hit.IsDirectory
                ? FolderItem(hit.Path, _app.Frecency.Score(hit.Path) > 0 ? Loc.T("Launcher.Frequent") : "", hit.Score)
                : FileItem(hit.Path, hit.Score);
        }
        var results = byPath.Values.OrderByDescending(r => r.Score).Take(30).ToList();

        // Typing an action's name ("organize", "settings") also offers that action, after the files.
        if (query.Length >= 3)
            results.AddRange(Actions(query).Take(3));
        return results;
    }

    private static bool LooksLikePath(string q) =>
        (q.Length >= 2 && q[1] == ':') || q.StartsWith(@"\\", StringComparison.Ordinal) || q.StartsWith('~');

    /// <summary>Typing a path lists that folder's contents, like an address bar with autocomplete.</summary>
    private List<ResultItem> BrowsePath(string query, CancellationToken ct)
    {
        string expanded = query.StartsWith('~') ? KnownFolders.UserProfile + query[1..] : query;
        string dir, filter;
        if (expanded.EndsWith('\\') || expanded.EndsWith('/')) { dir = expanded; filter = ""; }
        else if (expanded.Length == 2 && expanded[1] == ':') { dir = expanded + "\\"; filter = ""; }
        else { dir = Path.GetDirectoryName(expanded) ?? expanded; filter = Path.GetFileName(expanded); }

        var results = new List<ResultItem>();
        if (Directory.Exists(expanded) && filter.Length > 0) results.Add(FolderItem(expanded, "", 200));
        if (!Directory.Exists(dir)) return results;

        string token = filter.ToLowerInvariant();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        try
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
            {
                ct.ThrowIfCancellationRequested();
                double score = token.Length == 0 ? 50 : TextMatch.Score(entry.Name, token);
                if (score <= 0) continue;
                bool isDir = entry is DirectoryInfo;
                if (isDir) score += 5;
                if (string.Equals(entry.FullName, expanded, StringComparison.OrdinalIgnoreCase)) continue;
                results.Add(isDir ? FolderItem(entry.FullName, "", score) : FileItem(entry.FullName, score));
                if (results.Count >= 400) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).Take(60).ToList();
    }

    private ResultItem FolderItem(string path, string badge, double score = 0) => new()
    {
        Title = PathUtil.DisplayName(path),
        Subtitle = Display.ShortPath(Path.GetDirectoryName(path) ?? path),
        Glyph = Display.FolderGlyph,
        Badge = badge,
        Path = path,
        IsDirectory = true,
        Score = score,
    };

    private static ResultItem FileItem(string path, double score) => new()
    {
        Title = Path.GetFileName(path),
        Subtitle = Display.ShortPath(Path.GetDirectoryName(path) ?? ""),
        Glyph = Display.Glyph(path, false),
        Path = path,
        Score = score,
    };

    /// <summary>Things to do with the folder that was open in Explorer when Quick Jump was summoned.</summary>
    private IEnumerable<ResultItem> ContextActions()
    {
        if (_targetFolder is not { } folder) yield break;
        string name = PathUtil.DisplayName(folder);
        yield return MakeAction("", Loc.T("Action.OrganizeHere"), name, () => _app.ShowHub("organize", folder));
        yield return MakeAction("", Loc.T("Action.CleanupHere"), name, () => _app.ShowHub("cleanup", folder));
        yield return MakeAction("", Loc.T("Action.DuplicatesHere"), name, () => _app.ShowHub("duplicates", folder));
    }

    private List<ResultItem> Actions(string filter)
    {
        var all = new List<ResultItem>(ContextActions());
        all.Add(MakeAction("", Loc.T("Action.OrganizeDownloads"), "", () => _app.ShowHub("organize", KnownFolders.Downloads)));
        all.Add(MakeAction("", Loc.T("Action.SaveWorkspace"), "", () => _app.ShowHub("workspaces")));
        foreach (var w in _app.Workspaces.All)
        {
            var workspace = w;
            all.Add(MakeAction("", Loc.F("Action.OpenWorkspace", w.Name), string.Join(" · ", w.Folders.Select(PathUtil.DisplayName)),
                () => _app.OpenWorkspace(workspace), Loc.T("Launcher.Workspace")));
        }
        if (_app.Journal.LastUndoable is { } last)
            all.Add(MakeAction("", Loc.F("Action.Undo", last.Title), Display.DateAndTime(last.TimeUtc), _app.UndoLast));
        all.Add(MakeAction("", Loc.T("Action.OpenApp"), "", () => _app.ShowHub("home")));
        all.Add(MakeAction("", Loc.T("Action.Settings"), "", () => _app.ShowHub("settings")));
        all.Add(MakeAction("", Loc.T("Action.Quit"), "", _app.Quit));

        if (filter.Length == 0) return all;
        string token = filter.ToLowerInvariant();
        return all.Select(a => (Item: a, Score: TextMatch.Score(a.Title, token)))
            .Where(x => x.Score > 0 && (x.Score >= 50 || filter.Length < 3))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item)
            .ToList();
    }

    private static ResultItem MakeAction(string glyph, string title, string subtitle, Action run, string? badge = null) => new()
    {
        Title = title,
        Subtitle = subtitle,
        Glyph = glyph,
        Badge = badge ?? Loc.T("Launcher.Action"),
        Run = run,
    };

    // ───────────────────────────── input ─────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var items = Results.ItemsSource as List<ResultItem>;
        int count = items?.Count ?? 0;
        switch (e.Key)
        {
            case Key.Escape:
                HideLauncher(restoreFocus: true);
                e.Handled = true;
                break;
            case Key.Down or Key.Up or Key.PageDown or Key.PageUp when count > 0:
                int step = e.Key switch { Key.Down => 1, Key.Up => -1, Key.PageDown => 8, _ => -8 };
                int next = Results.SelectedIndex + step;
                Results.SelectedIndex = e.Key is Key.Down or Key.Up ? (next + count) % count : Math.Clamp(next, 0, count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                if (Results.SelectedItem is ResultItem item) Execute(item, Keyboard.Modifiers);
                e.Handled = true;
                break;
            case Key.Tab:
                // Step into the selected folder, address-bar style.
                if (Results.SelectedItem is ResultItem { IsDirectory: true, Path: { } dir })
                {
                    Query.Text = dir.EndsWith('\\') ? dir : dir + "\\";
                    Query.CaretIndex = Query.Text.Length;
                }
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && Query.SelectionLength == 0:
                if (Results.SelectedItem is ResultItem { Path: { } path })
                {
                    try
                    {
                        Clipboard.SetText(path);
                        Footer.Text = Loc.T("Launcher.Copied") + "  ·  " + path;
                        _footerReset.Stop();
                        _footerReset.Start();
                    }
                    catch (COMException) { } // clipboard briefly locked by another app
                }
                e.Handled = true;
                break;
        }
    }

    private void Results_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(Results, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (container?.DataContext is ResultItem item) Execute(item, Keyboard.Modifiers);
    }

    private void Execute(ResultItem item, ModifierKeys modifiers)
    {
        HideLauncher(restoreFocus: false);
        try
        {
            if (item.Run is not null) { item.Run(); return; }
            if (item.Path is not { } path) return;

            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                ExplorerBridge.Reveal(path);
            }
            else if (item.IsDirectory)
            {
                bool done = !modifiers.HasFlag(ModifierKeys.Shift) && _target switch
                {
                    Target.Explorer => ExplorerBridge.Navigate(_previous, path),
                    Target.FileDialog => ExplorerBridge.NavigateFileDialog(_previous, path),
                    _ => false,
                };
                if (!done) ExplorerBridge.OpenInNewWindow(path);
            }
            else if (_target == Target.FileDialog && Path.GetDirectoryName(path) is { } parent)
            {
                // In an Open/Save dialog, go to the file's folder; the user confirms the file there.
                ExplorerBridge.NavigateFileDialog(_previous, parent);
            }
            else
            {
                ExplorerBridge.OpenFile(path);
            }

            string folder = item.IsDirectory ? path : Path.GetDirectoryName(path) ?? path;
            _app.Frecency.Visit(folder, item.IsDirectory ? 1 : 0.3);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Quick Jump action");
        }
    }

    // ───────────────────────────── placement ─────────────────────────────

    /// <summary>Centers the box near the top of the monitor the user is working on, at that monitor's scale.</summary>
    private void PositionOnActiveMonitor()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        IntPtr monitor = _previous != IntPtr.Zero ? MonitorFromWindow(_previous, 2) : IntPtr.Zero;
        if (monitor == IntPtr.Zero && Native.GetCursorPos(out var cursor)) monitor = MonitorFromPoint(cursor, 2);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        var work = info.rcWork;
        int width = (int)(Width * scale);
        int x = work.Left + (work.Right - work.Left - width) / 2;
        int y = work.Top + (int)((work.Bottom - work.Top) * 0.18);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // NOSIZE | NOZORDER | NOACTIVATE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
