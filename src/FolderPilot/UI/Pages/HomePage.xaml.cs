using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FolderPilot.Core;
using FolderPilot.Core.Indexing;
using FolderPilot.Core.Insights;
using FolderPilot.Interop;
using FolderPilot.Localization;
using FolderPilot.Services;

namespace FolderPilot.UI.Pages;

public sealed record SuggestionCard(string Glyph, string Title, string Body, string ActionText, Action Run);

public partial class HomePage : UserControl
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private readonly App _app = App.Current;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public HomePage()
    {
        InitializeComponent();
        int hour = DateTime.Now.Hour;
        Greeting.Text = Loc.T(hour < 12 ? "Home.Morning" : hour < 18 ? "Home.Afternoon" : "Home.Evening");
        QuickJumpBody.Text = Loc.F("Home.QuickJump.Body", _app.HotkeyText);
        BuildFrequent();
        _statusTimer.Tick += (_, _) => UpdateStatus();
        Loaded += async (_, _) =>
        {
            UpdateStatus();
            _statusTimer.Start();
            await LoadSuggestions();
        };
        Unloaded += (_, _) =>
        {
            _statusTimer.Stop();
            _cts.Cancel();
        };
    }

    private async Task LoadSuggestions()
    {
        if (_app.CachedSuggestions is { } cached && DateTime.UtcNow - cached.At < CacheFor)
        {
            Show(cached.Items, scanning: false);
            return;
        }
        var items = new List<Suggestion>();
        try
        {
            var token = _cts.Token;
            items.AddRange(await Task.Run(() => SuggestionEngine.Quick(KnownFolders.Downloads, KnownFolders.Desktop), token));
            Show(items, scanning: true);
            var filter = _app.Settings.CreateFilter();
            int days = _app.Settings.UnusedDays;
            var unused = await Task.Run(() => SuggestionEngine.Unused(KnownFolders.PersonalFolders(), filter, days, token), token);
            if (unused is not null) items.Add(unused);
            _app.CachedSuggestions = (DateTime.UtcNow, items);
            Show(items, scanning: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Home suggestions");
            Show(items, scanning: false);
        }
    }

    private void Show(List<Suggestion> items, bool scanning)
    {
        int days = _app.Settings.UnusedDays;
        Suggestions.ItemsSource = items.Select(s => s.Kind switch
        {
            SuggestionKind.OrganizeFolder => new SuggestionCard("",
                Loc.F("Suggestion.Organize.Title", PathUtil.DisplayName(s.Path), s.Count),
                Loc.F("Suggestion.Organize.Body", s.Count, PathUtil.DisplayName(s.Path), Display.Size(s.Bytes)),
                Loc.T("Suggestion.Organize.Action"), () => _app.ShowHub("organize", s.Path)),
            SuggestionKind.OldInstallers => new SuggestionCard("",
                Loc.F("Suggestion.Installers.Title", s.Count),
                Loc.F("Suggestion.Installers.Body", s.Count, SuggestionEngine.InstallerAgeDays, Display.Size(s.Bytes)),
                Loc.T("Suggestion.Review"), () => _app.ShowHub("installers")),
            _ => new SuggestionCard("",
                Loc.F("Suggestion.Unused.Title", Display.Size(s.Bytes), days),
                Loc.F("Suggestion.Unused.Body", Display.Size(s.Bytes), days, s.Count),
                Loc.T("Suggestion.Review"), () => _app.ShowHub("unused")),
        }).ToList();
        Scanning.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        AllTidy.Visibility = !scanning && items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildFrequent()
    {
        Frequent.Children.Clear();
        foreach (var (path, _) in _app.Frecency.Top(16).Where(f => Directory.Exists(f.Path)).Take(12))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Style = (Style)FindResource("Glyph"), Text = Display.FolderGlyph, FontSize = 14, Foreground = (System.Windows.Media.Brush)FindResource("Brush.AccentText") });
            content.Children.Add(new TextBlock { Text = PathUtil.DisplayName(path), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var chip = new Button { Style = (Style)FindResource("ChipButton"), Content = content, ToolTip = path };
            System.Windows.Automation.AutomationProperties.SetName(chip, PathUtil.DisplayName(path));
            string folder = path;
            chip.Click += (_, _) =>
            {
                ExplorerBridge.OpenInNewWindow(folder);
                _app.Frecency.Visit(folder);
            };
            Frequent.Children.Add(chip);
        }
        FrequentEmpty.Visibility = Frequent.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStatus()
    {
        var index = _app.Index;
        string indexText = index.State == IndexState.Building
            ? Loc.F("Home.Status.Indexing", index.ItemsSoFar)
            : Loc.F("Home.Status.Index", index.ItemCount);
        Status.Text = $"{indexText}   ·   {Loc.F("Home.Status.Memory", Display.Size(MemoryTrimmer.CurrentUsage()))}";
    }

    private void TryQuickJump_Click(object sender, RoutedEventArgs e) => _app.ShowLauncher();

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SuggestionCard card }) card.Run();
    }
}
