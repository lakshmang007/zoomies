using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Zoomies.Core;
using Zoomies.Core.Indexing;
using Zoomies.Interop;
using Zoomies.Localization;
using Zoomies.Services;
using Zoomies.Themes;
using Microsoft.Win32;

namespace Zoomies.UI.Pages;

public partial class SettingsPage : UserControl
{
    private readonly App _app = App.Current;
    private readonly List<string> _roots;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Hotkey? _candidate;
    private bool _loadingTheme;

    public SettingsPage()
    {
        InitializeComponent();
        var s = _app.Settings;
        _loadingTheme = true;
        (s.Theme switch
        {
            ThemeManager.Sand => ThemeSand,
            ThemeManager.Midnight => ThemeMidnight,
            _ => ThemeSystem,
        }).IsChecked = true;
        _loadingTheme = false;
        HotkeyBox.Text = _app.HotkeyText;
        LearnSwitch.IsChecked = s.LearnFromExplorer;
        StartupSwitch.IsChecked = WindowsIntegration.StartsWithWindows;
        MenuSwitch.IsChecked = WindowsIntegration.HasContextMenu;
        _roots = [.. s.IndexRoots];
        SkipBox.Text = string.Join(", ", s.ExcludedFolderNames);
        DotSwitch.IsChecked = s.ExcludeDotFolders;
        HiddenSwitch.IsChecked = s.IncludeHiddenFiles;
        TidySwitch.IsChecked = s.AutoTidyDownloads;
        MemorySwitch.IsChecked = s.LowMemoryMode;
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        VersionText.Text = Loc.F("Settings.Version", version);
        UpdateSwitch.IsChecked = s.AutoUpdate;
        ShowUpdateState();
        ShowRoots();
        ShowFolderRules();

        _statusTimer.Tick += (_, _) => UpdateStatus();
        Loaded += (_, _) => { UpdateStatus(); _statusTimer.Start(); };
        Unloaded += (_, _) => _statusTimer.Stop();
    }

    // ───────────────────────────── appearance ─────────────────────────────

    /// <summary>Applies the theme straight away; every open window updates live.</summary>
    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingTheme || sender is not RadioButton { Tag: string theme } || theme == _app.Settings.Theme) return;
        _app.Settings.Theme = theme;
        _app.SaveSettings();
        ThemeManager.Apply(theme);
    }

    // ───────────────────────────── shortcut ─────────────────────────────

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => _app.SuspendHotkey(); // so pressing it here doesn't open Quick Jump

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (HotkeySave.IsKeyboardFocused) return; // about to save
        _app.ResumeHotkey();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) { e.Handled = false; return; }
        var candidate = new Hotkey(Keyboard.Modifiers, key);
        HotkeyBox.Text = Hotkey.IsModifierKey(key) ? candidate.ToString().Replace("+" + key, "+…") : candidate.ToString();
        _candidate = candidate.IsValid ? candidate : null;
        HotkeySave.IsEnabled = _candidate is not null;
    }

    private void HotkeySave_Click(object sender, RoutedEventArgs e)
    {
        if (_candidate is not { } hotkey) return;
        bool ok = _app.TryUseHotkey(hotkey);
        HotkeyStatus.Text = ok ? Loc.F("Settings.Hotkey.Saved", hotkey) : Loc.T("Settings.Hotkey.Failed");
        HotkeyStatus.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "Brush.Success" : "Brush.Danger");
        HotkeyBox.Text = _app.HotkeyText;
        HotkeySave.IsEnabled = false;
        _candidate = null;
    }

    private void Learn_Click(object sender, RoutedEventArgs e) => _app.SetLearnFromExplorer(LearnSwitch.IsChecked == true);

    // ───────────────────────────── integration ─────────────────────────────

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool on = StartupSwitch.IsChecked == true;
        WindowsIntegration.StartsWithWindows = on;
        _app.Settings.StartWithWindows = on;
        _app.SaveSettings();
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        bool on = MenuSwitch.IsChecked == true;
        WindowsIntegration.HasContextMenu = on;
        _app.Settings.ExplorerContextMenu = on;
        _app.SaveSettings();
    }

    // ───────────────────────────── index ─────────────────────────────

    private void ShowRoots()
    {
        RootsList.ItemsSource = null;
        RootsList.ItemsSource = _roots.ToList();
        DefaultRoots.Visibility = _roots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetRoots.Visibility = _roots.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = true, InitialDirectory = KnownFolders.UserProfile };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (string folder in dialog.FolderNames)
            if (!_roots.Contains(folder, StringComparer.OrdinalIgnoreCase)) _roots.Add(folder);
        ShowRoots();
    }

    private void RemoveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string folder }) _roots.Remove(folder);
        ShowRoots();
    }

    private void ResetRoots_Click(object sender, RoutedEventArgs e)
    {
        _roots.Clear();
        ShowRoots();
    }

    private void ApplyIndex_Click(object sender, RoutedEventArgs e)
    {
        var s = _app.Settings;
        s.IndexRoots = [.. _roots];
        s.ExcludedFolderNames = SkipBox.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        s.ExcludeDotFolders = DotSwitch.IsChecked == true;
        s.IncludeHiddenFiles = HiddenSwitch.IsChecked == true;
        _app.SaveSettings();
        _app.ApplyIndexSettings(TimeSpan.Zero);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var index = _app.Index;
        IndexStatus.Text = index.State == IndexState.Building
            ? Loc.F("Settings.Index.Building", index.ItemsSoFar)
            : Loc.F("Settings.Index.Status", index.ItemCount, Display.Size(index.MemoryBytes), index.LastBuildDuration.TotalSeconds);
        MemoryNow.Text = Loc.F("Settings.Memory.Now", Display.Size(MemoryTrimmer.CurrentUsage()));
    }

    // ───────────────────────────── organize folder names ─────────────────────────────

    private sealed record FolderRuleRow(string Extension, string Label, string Folder)
    {
        public override string ToString() => $"{Label}, {Folder}";
    }

    private void ShowFolderRules()
    {
        var rows = _app.Settings.OrganizeFolders
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new FolderRuleRow(kv.Key, kv.Key.Length == 0 ? Loc.T("Organize.Types.NoExtension") : kv.Key, kv.Value))
            .ToList();
        FolderRules.ItemsSource = rows;
        FolderRulesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RemoveFolderRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderRuleRow row }) return;
        _app.Settings.OrganizeFolders.Remove(row.Extension);
        _app.SaveSettings();
        ShowFolderRules();
    }

    // ───────────────────────────── automation & performance ─────────────────────────────

    private void Tidy_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.AutoTidyDownloads = TidySwitch.IsChecked == true;
        _app.SaveSettings();
        _app.ConfigureAutoTidy();
    }

    private void Memory_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.LowMemoryMode = MemorySwitch.IsChecked == true;
        MemoryTrimmer.Enabled = _app.Settings.LowMemoryMode;
        _app.SaveSettings();
    }

    // ───────────────────────────── updates ─────────────────────────────

    private void ShowUpdateState()
    {
        bool installed = Updater.IsInstalled;
        UpdateSwitch.IsEnabled = CheckUpdates.IsEnabled = installed;
        string? pending = installed ? Updater.PendingVersion : null;
        RestartUpdate.Visibility = pending is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateInfo.Text = !installed ? Loc.T("Settings.Update.NotInstalled")
            : pending is not null ? Loc.F("Settings.Update.Ready", pending)
            : "";
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.AutoUpdate = UpdateSwitch.IsChecked == true;
        _app.SaveSettings();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdates.IsEnabled = false;
        UpdateInfo.Text = Loc.T("Settings.Update.Checking");
        try
        {
            string? version = await Task.Run(Updater.CheckAndDownloadAsync);
            if (version is null) UpdateInfo.Text = Loc.T("Settings.Update.Latest");
            else ShowUpdateState();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update check");
            UpdateInfo.Text = Loc.T("Settings.Update.Failed");
        }
        finally { CheckUpdates.IsEnabled = true; }
    }

    private void RestartUpdate_Click(object sender, RoutedEventArgs e) => _app.RestartToUpdate();

    private void DataFolder_Click(object sender, RoutedEventArgs e) => ExplorerBridge.OpenInNewWindow(AppPaths.DataDir);
}
