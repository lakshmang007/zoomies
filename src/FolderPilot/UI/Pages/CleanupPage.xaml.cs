using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using FolderPilot.Core;
using FolderPilot.Core.Cleanup;
using FolderPilot.Core.History;
using FolderPilot.Core.Insights;
using FolderPilot.Core.Platform;
using FolderPilot.Interop;
using FolderPilot.Localization;
using FolderPilot.Services;
using Microsoft.Win32;

namespace FolderPilot.UI.Pages;

public enum CleanupMode { Unused, Duplicates, Large, Installers, Empty }

public sealed class CleanupRow : Observable
{
    private bool _isChecked;

    public required string Path { get; init; }
    public required long Size { get; init; }
    public bool IsDirectory { get; init; }
    public DateTime LastUsedUtc { get; init; }
    public int Group { get; init; }
    public string GroupHeader { get; init; } = "";
    public bool IsOldestInGroup { get; init; }

    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => Display.ShortPath(System.IO.Path.GetDirectoryName(Path) ?? "", 60);
    public string Glyph => Display.Glyph(Path, IsDirectory);
    public string SizeText => IsDirectory ? "" : Display.Size(Size);
    public string LastUsedText => Display.Date(LastUsedUtc);

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }
}

public partial class CleanupPage : UserControl
{
    private static readonly int[] PeriodDays = [30, 90, 180, 365, 730];
    private const long MB = 1024 * 1024;

    private readonly App _app = App.Current;
    private CleanupMode _mode;
    private IReadOnlyList<string> _roots;
    private bool _customScope;
    private CancellationTokenSource? _cts;
    private List<CleanupRow> _rows = [];
    private bool _bulkUpdate;
    private JournalEntry? _done;

    public CleanupPage(CleanupMode mode, string? folder, bool autoScan)
    {
        InitializeComponent();
        _customScope = folder is not null && Directory.Exists(folder);
        _roots = _customScope ? [folder!] : KnownFolders.PersonalFolders();

        foreach (int days in PeriodDays)
            PeriodBox.Items.Add(new ComboBoxItem { Content = Loc.T($"Cleanup.Period.{days}"), Tag = days });
        PeriodBox.SelectedIndex = Math.Max(0, Array.IndexOf(PeriodDays, _app.Settings.UnusedDays));
        PeriodBox.SelectionChanged += (_, _) =>
        {
            if (PeriodBox.SelectedItem is ComboBoxItem { Tag: int d } && d != _app.Settings.UnusedDays)
            {
                _app.Settings.UnusedDays = d;
                _app.SaveSettings();
                _app.CachedSuggestions = null;
            }
        };

        AccessNote.Text = Loc.T(Shell.IsLastAccessTracked() ? "Cleanup.AccessOn" : "Cleanup.AccessOff");
        UpdateScopeText();
        Unloaded += (_, _) => _cts?.Cancel();

        var tab = mode switch
        {
            CleanupMode.Duplicates => TabDuplicates,
            CleanupMode.Large => TabLarge,
            CleanupMode.Installers => TabInstallers,
            CleanupMode.Empty => TabEmpty,
            _ => TabUnused,
        };
        tab.IsChecked = true; // fires Tab_Checked, which sets up the options
        // Arriving from a suggestion or the right-click menu: start right away.
        if (autoScan) Loaded += (_, _) => Scan();
    }

    // ───────────────────────────── options ─────────────────────────────

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        _cts?.Cancel();
        _mode = Enum.Parse<CleanupMode>(tag);
        PeriodOption.Visibility = _mode == CleanupMode.Unused ? Visibility.Visible : Visibility.Collapsed;
        SizeOption.Visibility = _mode is CleanupMode.Unused or CleanupMode.Duplicates or CleanupMode.Large ? Visibility.Visible : Visibility.Collapsed;
        AccessNote.Visibility = _mode == CleanupMode.Unused ? Visibility.Visible : Visibility.Collapsed;

        long[] sizes = _mode == CleanupMode.Large ? [50 * MB, 100 * MB, 500 * MB, 1024 * MB] : [0, MB, 10 * MB, 100 * MB];
        long preferred = _mode == CleanupMode.Large ? 100 * MB : MB;
        SizeBox.Items.Clear();
        foreach (long s in sizes)
            SizeBox.Items.Add(new ComboBoxItem { Content = s == 0 ? Loc.T("Cleanup.AnySize") : Display.Size(s), Tag = s });
        SizeBox.SelectedIndex = Array.IndexOf(sizes, preferred);

        ResultsCard.Visibility = DoneBanner.Visibility = Visibility.Collapsed;
        UpdateScopeText();
    }

    private void UpdateScopeText()
    {
        bool installers = _mode == CleanupMode.Installers;
        ScopeText.Text = _customScope ? _roots[0] : installers ? KnownFolders.Downloads : Loc.T("Cleanup.MyFolders");
        ScopeText.ToolTip = string.Join(Environment.NewLine, _roots);
        ResetScopeButton.Visibility = _customScope ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = _customScope ? _roots[0] : KnownFolders.UserProfile };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _roots = [dialog.FolderName];
        _customScope = true;
        UpdateScopeText();
    }

    private void ResetScope_Click(object sender, RoutedEventArgs e)
    {
        _roots = KnownFolders.PersonalFolders();
        _customScope = false;
        UpdateScopeText();
    }

    // ───────────────────────────── scanning ─────────────────────────────

    private void Scan_Click(object sender, RoutedEventArgs e) => Scan();

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void Scan()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        var mode = _mode;
        var roots = _roots;
        var filter = _app.Settings.CreateFilter();
        int days = PeriodBox.SelectedItem is ComboBoxItem { Tag: int d } ? d : 180;
        long minSize = SizeBox.SelectedItem is ComboBoxItem { Tag: long s } ? s : MB;
        string installersFolder = _customScope ? roots[0] : KnownFolders.Downloads;
        var progress = new Progress<ScanProgress>(p => ProgressText.Text = Loc.F("Cleanup.Scanning", p.ItemsScanned));

        SetScanning(true);
        DoneBanner.Visibility = Visibility.Collapsed;
        string? error = null;
        try
        {
            var rows = await Task.Run(() => mode switch
            {
                CleanupMode.Unused => FileRows(UnusedFileScanner.Scan(roots, filter, days, Math.Max(1, minSize), progress, token)),
                CleanupMode.Large => FileRows(LargeFileScanner.Scan(roots, filter, minSize, progress, token)),
                CleanupMode.Installers => FileRows(SuggestionEngine.OldInstallers(installersFolder)),
                CleanupMode.Duplicates => DuplicateRows(DuplicateFinder.Find(roots, filter, Math.Max(1, minSize), progress, token)),
                _ => EmptyFolderScanner.Scan(roots, filter, progress, token).Select(p => new CleanupRow
                {
                    Path = p, Size = 0, IsDirectory = true, LastUsedUtc = SafeDirTime(p),
                }).ToList(),
            }, token);
            if (token.IsCancellationRequested || mode != _mode) return;
            ShowRows(rows);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Cleanup scan");
            error = Loc.F("Common.Error", ex.Message);
        }
        finally
        {
            if (_cts == cts)
            {
                SetScanning(false);
                if (error is not null) ProgressText.Text = error;
            }
        }
    }

    private void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        StopButton.Visibility = Progress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = "";
    }

    private static List<CleanupRow> FileRows(IEnumerable<FileCandidate> files) =>
        files.Select(f => new CleanupRow { Path = f.Path, Size = f.Size, LastUsedUtc = f.LastUsedUtc }).ToList();

    /// <summary>Every copy except the oldest starts selected: the original stays, the extra copies go.</summary>
    private static List<CleanupRow> DuplicateRows(List<DuplicateGroup> groups)
    {
        var rows = new List<CleanupRow>();
        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            string header = Loc.F("Cleanup.DuplicateGroup", group.Files.Count, Display.Size(group.Size));
            for (int i = 0; i < group.Files.Count; i++)
            {
                var f = group.Files[i];
                rows.Add(new CleanupRow
                {
                    Path = f.Path, Size = f.Size, LastUsedUtc = f.LastUsedUtc,
                    Group = g, GroupHeader = header, IsOldestInGroup = i == 0, IsChecked = i > 0,
                });
            }
        }
        return rows;
    }

    private static DateTime SafeDirTime(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private void ShowRows(List<CleanupRow> rows)
    {
        foreach (var row in _rows) row.PropertyChanged -= Row_PropertyChanged;
        _rows = rows;
        foreach (var row in _rows) row.PropertyChanged += Row_PropertyChanged;

        if (_mode == CleanupMode.Duplicates)
        {
            var view = new ListCollectionView(_rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CleanupRow.Group)));
            Rows.ItemsSource = view;
        }
        else Rows.ItemsSource = _rows;

        ResultsCard.Visibility = Visibility.Visible;
        bool any = _rows.Count > 0;
        NoneFound.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ListArea.Visibility = SelectionButtons.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        KeepOldestButton.Visibility = _mode == CleanupMode.Duplicates ? Visibility.Visible : Visibility.Collapsed;

        ResultSummary.Text = _mode == CleanupMode.Duplicates
            ? Loc.F("Cleanup.DuplicateSummary", _rows.Select(r => r.Group).Distinct().Count(),
                Display.Size(_rows.Where(r => !r.IsOldestInGroup).Sum(r => r.Size)))
            : Loc.F("Cleanup.ResultSummary", _rows.Count, Display.Size(_rows.Sum(r => r.Size)));
        UpdateSelection();
    }

    // ───────────────────────────── selection ─────────────────────────────

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_bulkUpdate && e.PropertyName == nameof(CleanupRow.IsChecked)) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = _rows.Where(r => r.IsChecked).ToList();
        SelectedText.Text = Loc.F("Cleanup.Selected", selected.Count, Display.Size(selected.Sum(r => r.Size)));
        RecycleButton.IsEnabled = selected.Count > 0;
    }

    private void SetAll(Func<CleanupRow, bool> isChecked)
    {
        _bulkUpdate = true;
        foreach (var row in _rows) row.IsChecked = isChecked(row);
        _bulkUpdate = false;
        UpdateSelection();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(_ => true);
    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAll(_ => false);
    private void KeepOldest_Click(object sender, RoutedEventArgs e) => SetAll(r => !r.IsOldestInGroup);

    private void Rows_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Rows.SelectedItem is CleanupRow row) ExplorerBridge.Reveal(row.Path);
    }

    // ───────────────────────────── recycle & undo ─────────────────────────────

    private async void Recycle_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsChecked).ToList();
        if (selected.Count == 0) return;
        var window = Window.GetWindow(this);
        long bytes = selected.Sum(r => r.Size);
        var answer = MessageBox.Show(window, Loc.F("Cleanup.Confirm.Body", selected.Count, Display.Size(bytes)),
            Loc.T("Cleanup.Confirm.Title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        RecycleButton.IsEnabled = false;
        IntPtr owner = new WindowInteropHelper(window).Handle;
        var items = selected.Select(r => (r.Path, r.Size)).ToList();
        string title = Loc.F("Journal.Recycled", items.Count);
        var outcome = await StaThread.Run(() => Recycler.Recycle(items, _app.Journal, title, owner));
        _app.CachedSuggestions = null;
        _done = outcome.Entry;

        var gone = selected.Where(r => !File.Exists(r.Path) && !Directory.Exists(r.Path)).ToHashSet();
        ShowRows(_rows.Where(r => !gone.Contains(r)).ToList());

        DoneText.Text = Loc.F("Cleanup.Done", outcome.Recycled, Display.Size(outcome.Bytes))
            + (outcome.Skipped > 0 ? " " + Loc.F("Cleanup.DoneSkipped", outcome.Skipped) : "");
        UndoButton.Visibility = _done is null ? Visibility.Collapsed : Visibility.Visible;
        DoneBanner.Visibility = Visibility.Visible;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_done is not { } entry) return;
        UndoButton.IsEnabled = false;
        var result = await Task.Run(() => _app.Journal.Undo(entry.Id));
        _done = null;
        _app.CachedSuggestions = null;
        DoneText.Text = result.Failed == 0
            ? Loc.F("History.UndoResult", result.Restored)
            : Loc.F("History.UndoPartial", result.Restored, result.Failed);
        UndoButton.Visibility = Visibility.Collapsed;
        UndoButton.IsEnabled = true;
    }
}
