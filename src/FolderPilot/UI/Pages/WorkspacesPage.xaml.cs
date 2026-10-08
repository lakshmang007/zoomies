using System.Windows;
using System.Windows.Controls;
using FolderPilot.Core;
using FolderPilot.Core.Workspaces;
using FolderPilot.Interop;
using FolderPilot.Localization;

namespace FolderPilot.UI.Pages;

public sealed record WorkspaceRow(Workspace Source, string Name, string Detail, string Folders);

public partial class WorkspacesPage : UserControl
{
    private readonly App _app = App.Current;
    private List<string> _open = [];

    public WorkspacesPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            if (Window.GetWindow(this) is { } w) w.Activated += Window_Activated;
        };
        Unloaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } w) w.Activated -= Window_Activated;
        };
    }

    // Coming back from File Explorer: recount the open folders.
    private void Window_Activated(object? sender, EventArgs e) => RefreshOpenFolders();

    private void Refresh()
    {
        RefreshOpenFolders();
        var all = _app.Workspaces.All;
        List.ItemsSource = all.Select(w => new WorkspaceRow(w, w.Name,
            $"{Loc.F("Workspaces.Folders", w.Folders.Count)} · {Display.DateAndTime(w.LastOpenedUtc ?? w.CreatedUtc)}",
            string.Join("   ", w.Folders.Select(PathUtil.DisplayName)))).ToList();
        EmptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshOpenFolders()
    {
        _open = ExplorerBridge.GetOpenFolders();
        OpenCount.Text = _open.Count == 0 ? Loc.T("Workspaces.NoneOpen") : Loc.F("Workspaces.OpenCount", _open.Count);
        SaveButton.IsEnabled = _open.Count > 0;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        NamePlaceholder.Visibility = NameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        RefreshOpenFolders();
        if (_open.Count == 0) return;
        string name = NameBox.Text.Trim();
        if (name.Length == 0) name = Loc.F("Workspaces.DefaultName", _app.Workspaces.All.Count + 1);
        var saved = _app.Workspaces.Save(name, _open);
        foreach (string folder in saved.Folders) _app.Frecency.Visit(folder, 0.5);
        NameBox.Text = "";
        SavedText.Text = Loc.F("Workspaces.Saved", saved.Name, saved.Folders.Count);
        SavedText.Visibility = Visibility.Visible;
        Refresh();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceRow row }) _app.OpenWorkspace(row.Source);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkspaceRow row }) return;
        _app.Workspaces.Delete(row.Name);
        Refresh();
    }
}
