using System.Windows;
using System.Windows.Controls;
using FolderPilot.Localization;
using FolderPilot.Themes;
using FolderPilot.UI.Pages;

namespace FolderPilot.UI;

public partial class HubWindow : Window
{
    private bool _navigating;

    public HubWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.StyleWindow(this);
        Activated += (_, _) => HotkeyHint.Text = Loc.F("Nav.ShortcutHint", App.Current.HotkeyText);
    }

    /// <param name="page">home, organize, cleanup, unused, duplicates, installers, workspaces, history or settings.</param>
    public void Navigate(string page, string? argument = null)
    {
        string navKey = page is "unused" or "duplicates" or "installers" ? "cleanup" : page;
        _navigating = true;
        Nav.SelectedItem = Nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == navKey) ?? Nav.Items[0];
        _navigating = false;

        // The old page is dropped (and its scans cancelled) so only one page lives in memory.
        PageHost.Content = page switch
        {
            "organize" => new OrganizePage(argument),
            "cleanup" => new CleanupPage(CleanupMode.Unused, argument, autoScan: argument is not null),
            "unused" => new CleanupPage(CleanupMode.Unused, argument, autoScan: true),
            "duplicates" => new CleanupPage(CleanupMode.Duplicates, argument, autoScan: true),
            "installers" => new CleanupPage(CleanupMode.Installers, argument, autoScan: true),
            "workspaces" => new WorkspacesPage(),
            "history" => new HistoryPage(),
            "settings" => new SettingsPage(),
            _ => new HomePage(),
        };
        HotkeyHint.Text = Loc.F("Nav.ShortcutHint", App.Current.HotkeyText);
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navigating || Nav.SelectedItem is not ListBoxItem { Tag: string page }) return;
        Navigate(page);
    }
}
