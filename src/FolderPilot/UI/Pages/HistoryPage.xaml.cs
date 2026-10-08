using System.Windows;
using System.Windows.Controls;
using FolderPilot.Core.History;
using FolderPilot.Localization;

namespace FolderPilot.UI.Pages;

public sealed record HistoryRow(Guid Id, string Glyph, string Title, string Detail, Visibility UndoVisibility, Visibility UndoneVisibility);

public partial class HistoryPage : UserControl
{
    private readonly App _app = App.Current;

    public HistoryPage()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var entries = _app.Journal.Entries;
        List.ItemsSource = entries.Select(e => new HistoryRow(
            e.Id,
            e.Kind == JournalKind.Move ? "" : "",
            e.Title,
            $"{Loc.F("History.Items", e.Ops.Count, Display.Size(e.TotalBytes))} · {Display.DateAndTime(e.TimeUtc)}",
            e.Undone ? Visibility.Collapsed : Visibility.Visible,
            e.Undone ? Visibility.Visible : Visibility.Collapsed)).ToList();
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListCard.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistoryRow row } button) return;
        button.IsEnabled = false;
        var result = await Task.Run(() => _app.Journal.Undo(row.Id));
        _app.CachedSuggestions = null;
        ResultText.Text = result.Failed == 0
            ? Loc.F("History.UndoResult", result.Restored)
            : Loc.F("History.UndoPartial", result.Restored, result.Failed);
        ResultBanner.Visibility = Visibility.Visible;
        Refresh();
    }
}
