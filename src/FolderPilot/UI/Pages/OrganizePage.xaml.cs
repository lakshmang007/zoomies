using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FolderPilot.Core;
using FolderPilot.Core.History;
using FolderPilot.Core.Organize;
using FolderPilot.Localization;
using Microsoft.Win32;

namespace FolderPilot.UI.Pages;

public sealed record MoveRow(string Glyph, string Name, string To, string Size);

public partial class OrganizePage : UserControl
{
    private readonly App _app = App.Current;
    private OrganizePlan? _plan;
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
        var options = new OrganizeOptions { Mode = mode, KeepShortcuts = KeepShortcuts.IsChecked == true };
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

        ShowPlan(_plan);
    }

    private void ShowPlan(OrganizePlan plan)
    {
        PlanCard.Visibility = Visibility.Visible;
        bool any = plan.Moves.Count > 0;
        Summary.Text = any
            ? Loc.F("Organize.Summary", plan.Moves.Count, plan.FolderCount, Display.Size(plan.TotalBytes))
            : Loc.T("Organize.Nothing");
        CategoryChips.ItemsSource = plan.Moves.GroupBy(m => m.Category)
            .Select(g => $"{App.CategoryFolderName(g.Key)} · {g.Count():N0}")
            .ToList();
        SkippedText.Text = plan.Skipped > 0 ? Loc.F("Organize.Skipped", plan.Skipped) : "";
        SkippedText.Visibility = plan.Skipped > 0 ? Visibility.Visible : Visibility.Collapsed;
        Moves.ItemsSource = plan.Moves.Select(m => new MoveRow(
            Display.Glyph(m.Source, false),
            Path.GetFileName(m.Source),
            Path.GetRelativePath(plan.Folder, Path.GetDirectoryName(m.Destination)!),
            Display.Size(m.Size))).ToList();
        ListHeader.Visibility = Moves.Visibility = ApplyButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = any;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is not { } plan) return;
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
