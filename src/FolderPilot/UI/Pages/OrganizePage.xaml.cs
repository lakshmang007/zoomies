using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FolderPilot.Core;
using FolderPilot.Core.History;
using FolderPilot.Core.Organize;
using FolderPilot.Localization;
using Microsoft.Win32;

namespace FolderPilot.UI.Pages;

public sealed record MoveRow(string Glyph, string Name, string To, string Size)
{
    /// <summary>What screen readers announce for the row.</summary>
    public override string ToString() => $"{Name}, {To}, {Size}";
}

/// <summary>One file type in the preview, with the folder those files will go to. The user can rename that folder.</summary>
public sealed class TypeRow : Observable
{
    private string _folder = "";
    private Visibility _rowVisibility = Visibility.Visible;

    /// <summary>
    /// Rows that don't match the search are hidden rather than removed: rebuilding the list would leave
    /// screen readers (UI Automation) unable to reach the folder boxes in the new rows.
    /// </summary>
    public Visibility RowVisibility
    {
        get => _rowVisibility;
        set => Set(ref _rowVisibility, value);
    }

    public override string ToString() => $"{Label}, {CountText}, {SizeText}, {Folder}";

    public required string Extension { get; init; }
    public required string Label { get; init; }
    public required string CountText { get; init; }
    public required string SizeText { get; init; }
    public required string DefaultFolder { get; init; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!Set(ref _folder, value)) return;
            Raise(nameof(ResetVisibility));
            Changed?.Invoke(this);
        }
    }

    public bool IsValid => OrganizePlanner.IsValidFolderName(Folder);
    public bool IsCustom => !Folder.Trim().Equals(DefaultFolder, StringComparison.CurrentCultureIgnoreCase);
    public Visibility ResetVisibility => IsCustom ? Visibility.Visible : Visibility.Collapsed;
    public string FolderAutomationName => Loc.F("Organize.Types.FolderFor", Label);

    public event Action<TypeRow>? Changed;
}

public partial class OrganizePage : UserControl
{
    private readonly App _app = App.Current;
    private readonly DispatcherTimer _retarget = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private OrganizePlan? _plan;
    private OrganizeOptions? _options;
    /// <summary>Working copy of the user's folder names (".stl" → "3D Prints"); saved on Organize if "Remember" is on.</summary>
    private Dictionary<string, string> _folders = [];
    private List<TypeRow> _typeRows = [];
    private string _filter = "";
    private JournalEntry? _done;

    public OrganizePage(string? folder)
    {
        InitializeComponent();
        FolderBox.Text = folder ?? KnownFolders.Downloads;
        (_app.Settings.OrganizeMode switch
        {
            OrganizeMode.ByTypeAndMonth => ModeByTypeAndMonth,
            OrganizeMode.ByMonth => ModeByMonth,
            _ => ModeByType,
        }).IsChecked = true;

        foreach (string quick in new[] { KnownFolders.Downloads, KnownFolders.Desktop, KnownFolders.Documents }.Where(Directory.Exists))
        {
            var chip = new Button { Style = (Style)FindResource("ChipButton"), Content = PathUtil.DisplayName(quick), ToolTip = quick };
            string path = quick;
            chip.Click += (_, _) => { FolderBox.Text = path; Preview(); };
            QuickFolders.Children.Add(chip);
        }

        _retarget.Tick += (_, _) =>
        {
            _retarget.Stop();
            if (_plan is null || _options is null) return;
            _plan = OrganizePlanner.Retarget(_plan, _options, App.CategoryFolderName);
            ShowPlan();
        };
        Unloaded += (_, _) => _retarget.Stop();
        if (folder is not null) Loaded += (_, _) => Preview();
    }

    private OrganizeMode SelectedMode =>
        ModeByTypeAndMonth.IsChecked == true ? OrganizeMode.ByTypeAndMonth
        : ModeByMonth.IsChecked == true ? OrganizeMode.ByMonth
        : OrganizeMode.ByType;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : KnownFolders.UserProfile };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            FolderBox.Text = dialog.FolderName;
            Preview();
        }
    }

    private void FolderBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Preview();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => Preview();

    private async void Preview()
    {
        string folder = FolderBox.Text.Trim().Trim('"');
        DoneBanner.Visibility = Visibility.Collapsed;
        if (!Directory.Exists(folder))
        {
            ErrorText.Text = Loc.T("Organize.FolderMissing");
            ErrorText.Visibility = Visibility.Visible;
            PlanCard.Visibility = Visibility.Collapsed;
            return;
        }
        ErrorText.Visibility = Visibility.Collapsed;

        var mode = SelectedMode;
        if (mode != _app.Settings.OrganizeMode)
        {
            _app.Settings.OrganizeMode = mode;
            _app.SaveSettings();
        }
        _folders = new Dictionary<string, string>(_app.Settings.OrganizeFolders, StringComparer.OrdinalIgnoreCase);
        var options = _options = new OrganizeOptions { Mode = mode, KeepShortcuts = KeepShortcuts.IsChecked == true, CustomFolders = _folders };
        PreviewButton.IsEnabled = false;
        try
        {
            _plan = await Task.Run(() => OrganizePlanner.Plan(folder, options, App.CategoryFolderName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = Loc.F("Common.Error", ex.Message);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        finally { PreviewButton.IsEnabled = true; }

        SearchBox.Text = "";
        BuildTypeRows();
        ShowPlan();
    }

    // ───────────────────────────── file types ─────────────────────────────

    private void BuildTypeRows()
    {
        foreach (var row in _typeRows) row.Changed -= TypeRow_Changed;
        _typeRows = [];
        if (_plan is null || _options is null) return;

        foreach (var group in _plan.Moves.GroupBy(m => m.Extension).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            var category = group.First().Category;
            var row = new TypeRow
            {
                Extension = group.Key,
                Label = group.Key.Length == 0 ? Loc.T("Organize.Types.NoExtension") : group.Key,
                CountText = group.Count().ToString("N0", Loc.Culture),
                SizeText = Display.Size(group.Sum(m => m.Size)),
                DefaultFolder = App.CategoryFolderName(category),
                Folder = OrganizePlanner.TypeFolder(group.Key, category, _options, App.CategoryFolderName),
            };
            row.Changed += TypeRow_Changed;
            _typeRows.Add(row);
        }
        // Custom names only apply when sorting by type; "by month" has no type folders.
        TypesPanel.Visibility = _options.Mode != OrganizeMode.ByMonth && _typeRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowTypeRows();
    }

    private void ShowTypeRows()
    {
        if (!ReferenceEquals(TypeRows.ItemsSource, _typeRows)) TypeRows.ItemsSource = _typeRows;
        foreach (var row in _typeRows)
            row.RowVisibility = _filter.Length == 0 || row.Label.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void TypeRow_Changed(TypeRow row)
    {
        if (row.IsValid)
        {
            // Back to the default name means "no custom rule" for that type.
            if (row.IsCustom) _folders[row.Extension] = row.Folder.Trim();
            else _folders.Remove(row.Extension);
        }
        bool anyInvalid = _typeRows.Any(r => !r.IsValid);
        TypeError.Visibility = anyInvalid ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !anyInvalid && _plan?.Moves.Count > 0;
        _retarget.Stop();
        _retarget.Start();
    }

    private void ResetType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TypeRow row }) row.Folder = row.DefaultFolder;
    }

    /// <summary>For screenshots: search for a type and rename its folder, the way a user would.</summary>
    internal void SimulateRename(string search, string extension, string folder)
    {
        SearchBox.Text = search;
        if (_typeRows.FirstOrDefault(r => r.Extension == extension) is { } row) row.Folder = folder;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        // "stl", ".stl" and "*.stl" all mean the .stl type.
        _filter = SearchBox.Text.Trim().TrimStart('*');
        ShowTypeRows();
        if (_plan is not null) ShowFiles();
    }

    // ───────────────────────────── preview ─────────────────────────────

    private void ShowPlan()
    {
        if (_plan is not { } plan) return;
        PlanCard.Visibility = Visibility.Visible;
        bool any = plan.Moves.Count > 0;
        Summary.Text = any
            ? Loc.F("Organize.Summary", plan.Moves.Count, plan.FolderCount, Display.Size(plan.TotalBytes))
            : Loc.T("Organize.Nothing");
        // One chip per top-level folder that will hold files, using the user's own names.
        CategoryChips.ItemsSource = plan.Moves
            .GroupBy(m => TopFolder(plan.Folder, m.Destination), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => $"{g.Key} · {g.Count():N0}")
            .ToList();
        SkippedText.Text = plan.Skipped > 0 ? Loc.F("Organize.Skipped", plan.Skipped) : "";
        SkippedText.Visibility = plan.Skipped > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchArea.Visibility = ListHeader.Visibility = Moves.Visibility = RememberBox.Visibility = ApplyButton.Visibility =
            any ? Visibility.Visible : Visibility.Collapsed;
        if (!any) TypesPanel.Visibility = Visibility.Collapsed;
        ApplyButton.IsEnabled = any && _typeRows.All(r => r.IsValid);
        ShowFiles();
    }

    private void ShowFiles()
    {
        if (_plan is not { } plan) return;
        string dotFilter = _filter.StartsWith('.') ? _filter : "." + _filter;
        var rows = plan.Moves
            .Where(m => _filter.Length == 0
                        || m.Extension.Equals(dotFilter, StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(m.Source).Contains(_filter, StringComparison.CurrentCultureIgnoreCase))
            .Select(m => new MoveRow(
                Display.Glyph(m.Source, false),
                Path.GetFileName(m.Source),
                Path.GetRelativePath(plan.Folder, Path.GetDirectoryName(m.Destination)!),
                Display.Size(m.Size)))
            .ToList();
        Moves.ItemsSource = rows;
        NoMatchText.Text = Loc.F("Organize.NoMatch", _filter);
        NoMatchText.Visibility = rows.Count == 0 && plan.Moves.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string TopFolder(string root, string destination)
    {
        string relative = Path.GetRelativePath(root, Path.GetDirectoryName(destination)!);
        int sep = relative.IndexOf(Path.DirectorySeparatorChar);
        return sep < 0 ? relative : relative[..sep];
    }

    // ───────────────────────────── apply & undo ─────────────────────────────

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is null || _options is null || _typeRows.Any(r => !r.IsValid)) return;
        _retarget.Stop();
        var plan = OrganizePlanner.Retarget(_plan, _options, App.CategoryFolderName); // include the very latest edits

        if (RememberBox.IsChecked == true)
        {
            _app.Settings.OrganizeFolders = new Dictionary<string, string>(_folders, StringComparer.OrdinalIgnoreCase);
            _app.SaveSettings();
        }

        ApplyButton.IsEnabled = PreviewButton.IsEnabled = false;
        string title = Loc.F("Journal.Organized", PathUtil.DisplayName(plan.Folder));
        var result = await Task.Run(() => FileMover.Execute(plan, _app.Journal, title));
        _app.CachedSuggestions = null;
        _done = result.Entry;
        _plan = null;

        PlanCard.Visibility = Visibility.Collapsed;
        PreviewButton.IsEnabled = true;
        DoneText.Text = result.Failed == 0
            ? Loc.F("Organize.Done", result.Moved)
            : Loc.F("Organize.DoneWithFailures", result.Moved, result.Failed);
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
            ? Loc.T("Organize.Undone")
            : Loc.F("History.UndoPartial", result.Restored, result.Failed);
        UndoButton.Visibility = Visibility.Collapsed;
        UndoButton.IsEnabled = true;
    }
}
