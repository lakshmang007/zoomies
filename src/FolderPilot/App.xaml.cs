using System.IO;
using System.Windows;
using System.Windows.Threading;
using FolderPilot.Core;
using FolderPilot.Core.Frecency;
using FolderPilot.Core.History;
using FolderPilot.Core.Indexing;
using FolderPilot.Core.Insights;
using FolderPilot.Core.Organize;
using FolderPilot.Core.Settings;
using FolderPilot.Core.Workspaces;
using FolderPilot.Interop;
using FolderPilot.Localization;
using FolderPilot.Services;
using FolderPilot.Themes;
using FolderPilot.UI;

namespace FolderPilot;

public partial class App : Application
{
    private const string MutexName = @"Local\FolderPilot.Instance.6c1f";

    private Mutex? _instanceMutex;
    private MessageHost? _host;
    private TrayIcon? _tray;
    private HotkeyRegistration? _hotkey;
    private ExplorerTracker? _tracker;
    private DispatcherTimer? _housekeeping;
    private DispatcherTimer? _autoTidy;
    private LauncherWindow? _launcher;
    private HubWindow? _hub;

    public static new App Current => (App)Application.Current;

    public AppSettings Settings { get; private set; } = new();
    public FrecencyStore Frecency { get; private set; } = null!;
    public OperationJournal Journal { get; private set; } = null!;
    public WorkspaceStore Workspaces { get; private set; } = null!;
    public IndexService Index { get; } = new();

    /// <summary>Home-page suggestions are reused for a few minutes instead of rescanning on every visit.</summary>
    public (DateTime At, List<Suggestion> Items)? CachedSuggestions { get; set; }

    public string HotkeyText => _hotkey?.Current?.ToString() ?? Settings.Hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception, "UI thread");
            ex.Handled = true; // keep running; one failed action should never take the tray app down
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error((Exception)ex.ExceptionObject, "Background thread");
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error(ex.Exception, "Task"); ex.SetObserved(); };

        // Demo mode (videos, screenshots): sample folders and separate app data, so no real file names ever appear.
        if (Environment.GetEnvironmentVariable("FOLDERPILOT_DEMO_ROOT") is { Length: > 0 } demoRoot && Directory.Exists(demoRoot))
        {
            KnownFolders.UseDemoRoot(demoRoot);
            AppPaths.OverrideDataDir(Path.Combine(demoRoot, ".folderpilot"));
        }

        string[] args = e.Args;
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            Snapshot.Run(this, args[1], args.Contains("--dark"));
            return;
        }

        _instanceMutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
        {
            MessageHost.SendToRunningInstance(args);
            Shutdown();
            return;
        }

        InitializeCore();

        _host = new MessageHost();
        _host.Hotkey += _ => ShowLauncher();
        _host.CommandReceived += HandleArgs;
        _host.ThemeChanged += () => ThemeManager.Apply();

        _tray = new TrayIcon(_host, Path.Combine(AppContext.BaseDirectory, "Assets", "FolderPilot.ico"), "FolderPilot");
        _tray.Click += () => ShowHub("home");
        _tray.MenuRequested += ShowTrayMenu;
        _tray.Show();

        _hotkey = new HotkeyRegistration(_host);
        RegisterStartupHotkey();

        _tracker = new ExplorerTracker(Frecency) { Enabled = Settings.LearnFromExplorer };

        bool background = args.Contains("--background");
        // At sign-in the disk is busy, so indexing waits a little longer.
        ApplyIndexSettings(background ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1));
        // Seeding reads Windows' real Recent list, and registrations touch the real registry: neither belongs in a demo.
        if (!KnownFolders.IsDemo)
        {
            if (Frecency.Count == 0) FolderSeeder.SeedInBackground(Frecency, () => { });
            else Task.Run(() => Frecency.Prune());
            WindowsIntegration.Refresh(Settings.StartWithWindows, Settings.ExplorerContextMenu);
        }

        _housekeeping = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
        _housekeeping.Tick += (_, _) => Frecency.SaveIfDirty();
        _housekeeping.Start();
        ConfigureAutoTidy();

        if (!Settings.HasCompletedWelcome)
        {
            _tray.Notify(Loc.T("Notify.Running.Title"), Loc.F("Notify.Running.Body", HotkeyText));
            Settings.HasCompletedWelcome = true;
            SaveSettings();
        }

        if (!background) HandleArgs(args);
        MemoryTrimmer.TrimSoon(TimeSpan.FromSeconds(30));
    }

    /// <summary>Settings, language, theme and data stores. Shared with snapshot mode.</summary>
    internal void InitializeCore()
    {
        Settings = SettingsStore.Load();
        Loc.Init(Settings.Language);
        ThemeManager.Apply(Settings.Theme);
        Native.FollowSystemThemeInMenus();
        MemoryTrimmer.Enabled = Settings.LowMemoryMode;
        Frecency = new FrecencyStore();
        Journal = new OperationJournal();
        Workspaces = new WorkspaceStore();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Frecency?.SaveIfDirty();
        _tracker?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        Index.Dispose();
        _host?.Dispose();
        if (_instanceMutex is not null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }

    // ───────────────────────────── commands ─────────────────────────────

    /// <summary>Command-line arguments, from this launch or forwarded by a second launch (e.g. the Explorer right-click menu).</summary>
    private void HandleArgs(string[] args)
    {
        string? Arg(int i) => args.Length > i ? args[i] : null;
        switch (Arg(0))
        {
            case "--background": break;
            case "--quit": Quit(); break; // used by installers and updaters to close the running copy cleanly
            case "--launcher": ShowLauncher(); break;
            case "--organize": ShowHub("organize", Arg(1)); break;
            case "--cleanup": ShowHub("cleanup", Arg(1)); break;
            case "--duplicates": ShowHub("duplicates", Arg(1)); break;
            case "--settings": ShowHub("settings"); break;
            default: ShowHub("home"); break;
        }
    }

    public void ShowLauncher()
    {
        IntPtr previous = Native.GetForegroundWindow();
        _launcher ??= new LauncherWindow();
        _launcher.ShowFor(previous);
    }

    public void ShowHub(string page, string? argument = null)
    {
        MemoryTrimmer.Cancel();
        if (_hub is null)
        {
            _hub = new HubWindow();
            _hub.Closed += (_, _) =>
            {
                _hub = null;
                // The window and its pages are released; give the memory back once things settle.
                MemoryTrimmer.TrimSoon(TimeSpan.FromSeconds(5));
            };
            _hub.Show();
        }
        _hub.Navigate(page, argument);
        if (_hub.WindowState == WindowState.Minimized) _hub.WindowState = WindowState.Normal;
        _hub.Activate();
        Native.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_hub).Handle);
    }

    public void OpenWorkspace(Workspace workspace)
    {
        foreach (string folder in workspace.Folders.Where(Directory.Exists))
        {
            ExplorerBridge.OpenInNewWindow(folder);
            Frecency.Visit(folder, 0.5);
        }
        Workspaces.MarkOpened(workspace.Name);
    }

    public async void UndoLast()
    {
        var entry = Journal.LastUndoable;
        if (entry is null) return;
        var result = await Task.Run(() => Journal.Undo(entry.Id));
        Notify(Loc.T("Notify.Undone.Title"), result.Failed == 0
            ? $"{entry.Title} — {Loc.P("History.UndoResult", result.Restored)}"
            : Loc.F("History.UndoPartial", result.Restored, result.Failed));
    }

    public void Notify(string title, string body) => _tray?.Notify(title, body);

    public void Quit()
    {
        _launcher?.Close();
        _hub?.Close();
        Shutdown();
    }

    // ───────────────────────────── settings ─────────────────────────────

    public void SaveSettings()
    {
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error(ex, "Saving settings"); }
        UpdateTooltip();
    }

    public void ApplyIndexSettings(TimeSpan delay) =>
        Index.Configure(Settings.EffectiveIndexRoots(), Settings.CreateFilter(), Settings.MaxIndexedItems, delay);

    public bool TryUseHotkey(Hotkey hotkey)
    {
        if (_hotkey is null || !_hotkey.Register(hotkey))
        {
            RegisterStartupHotkey(notify: false); // put the previous one back
            return false;
        }
        Settings.Hotkey = hotkey.ToString();
        SaveSettings();
        return true;
    }

    /// <summary>While the user records a new shortcut, the current one must not fire.</summary>
    public void SuspendHotkey() => _hotkey?.Unregister();

    public void ResumeHotkey()
    {
        if (_hotkey?.Current is null) RegisterStartupHotkey(notify: false);
    }

    public void SetLearnFromExplorer(bool on)
    {
        Settings.LearnFromExplorer = on;
        if (_tracker is not null) _tracker.Enabled = on;
        SaveSettings();
    }

    public void ConfigureAutoTidy()
    {
        if (!Settings.AutoTidyDownloads)
        {
            _autoTidy?.Stop();
            return;
        }
        if (_autoTidy is null)
        {
            _autoTidy = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(15) };
            _autoTidy.Tick += async (_, _) => await AutoTidyDownloads();
        }
        _autoTidy.Start();
    }

    /// <summary>Sorts finished downloads by type. Anything changed in the last 30 minutes is left alone, so files still in use are safe.</summary>
    private async Task AutoTidyDownloads()
    {
        string downloads = KnownFolders.Downloads;
        if (!Directory.Exists(downloads)) return;
        string title = Loc.F("Journal.AutoTidy", PathUtil.DisplayName(downloads));
        var options = new OrganizeOptions
        {
            Mode = OrganizeMode.ByType,
            SkipRecentMinutes = 30,
            CustomFolders = new Dictionary<string, string>(Settings.OrganizeFolders),
        };
        var result = await Task.Run(() =>
        {
            var plan = OrganizePlanner.Plan(downloads, options, CategoryFolderName);
            return plan.Moves.Count == 0 ? default : FileMover.Execute(plan, Journal, title);
        });
        if (result.Moved > 0) Notify(Loc.T("Notify.AutoTidy.Title"), Loc.F("Notify.AutoTidy.Body", result.Moved));
    }

    public static string CategoryFolderName(FileCategory category) => Loc.T("Category." + category);

    private void RegisterStartupHotkey(bool notify = true)
    {
        if (_hotkey is null) return;
        if (!Hotkey.TryParse(Settings.Hotkey, out var preferred)) preferred = Hotkey.Default;
        if (_hotkey.Register(preferred)) { UpdateTooltip(); return; }
        foreach (var alternative in Hotkey.Alternatives)
        {
            if (alternative == preferred || !_hotkey.Register(alternative)) continue;
            break;
        }
        if (notify) Notify(Loc.T("Notify.HotkeyFailed.Title"), Loc.F("Notify.HotkeyFailed.Body", preferred));
        UpdateTooltip();
    }

    private void UpdateTooltip() => _tray?.SetTooltip(Loc.F("Tray.Tooltip", HotkeyText));

    // ───────────────────────────── tray menu ─────────────────────────────

    private void ShowTrayMenu(int x, int y)
    {
        const int QuickJump = 1, Open = 2, Organize = 3, Unused = 4, Undo = 5, SettingsId = 6, QuitId = 7, WorkspaceBase = 100;
        var workspaces = Workspaces.All.Take(20).ToList();
        var last = Journal.LastUndoable;

        IntPtr menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, Native.MF_STRING, QuickJump, Loc.F("Tray.QuickJump", HotkeyText));
        Native.AppendMenu(menu, Native.MF_STRING, Open, Loc.T("Tray.Open"));
        Native.SetMenuDefaultItem(menu, Open, 0);
        Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
        Native.AppendMenu(menu, Native.MF_STRING, Organize, Loc.T("Tray.OrganizeDownloads"));
        Native.AppendMenu(menu, Native.MF_STRING, Unused, Loc.T("Tray.FindUnused"));

        IntPtr sub = Native.CreatePopupMenu();
        if (workspaces.Count == 0) Native.AppendMenu(sub, Native.MF_STRING | Native.MF_GRAYED, 0, Loc.T("Tray.NoWorkspaces"));
        for (int i = 0; i < workspaces.Count; i++) Native.AppendMenu(sub, Native.MF_STRING, WorkspaceBase + i, workspaces[i].Name);
        Native.AppendMenu(menu, Native.MF_POPUP, sub, Loc.T("Tray.Workspaces"));

        if (last is not null) Native.AppendMenu(menu, Native.MF_STRING, Undo, Loc.F("Tray.Undo", last.Title));
        else Native.AppendMenu(menu, Native.MF_STRING | Native.MF_GRAYED, 0, Loc.T("Tray.UndoNothing"));
        Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
        Native.AppendMenu(menu, Native.MF_STRING, SettingsId, Loc.T("Tray.Settings"));
        Native.AppendMenu(menu, Native.MF_STRING, QuitId, Loc.T("Tray.Quit"));

        // The menu only closes on an outside click if our window is in front first (documented Win32 quirk).
        Native.SetForegroundWindow(_host!.Handle);
        int command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN, x, y, _host.Handle, IntPtr.Zero);
        Native.PostMessage(_host.Handle, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        Native.DestroyMenu(menu);

        switch (command)
        {
            case QuickJump: ShowLauncher(); break;
            case Open: ShowHub("home"); break;
            case Organize: ShowHub("organize", KnownFolders.Downloads); break;
            case Unused: ShowHub("unused"); break;
            case Undo: UndoLast(); break;
            case SettingsId: ShowHub("settings"); break;
            case QuitId: Quit(); break;
            case >= WorkspaceBase when command - WorkspaceBase < workspaces.Count:
                OpenWorkspace(workspaces[command - WorkspaceBase]);
                break;
        }
    }
}
