using Zoomies.Core.Organize;

namespace Zoomies.Core.Settings;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Global shortcut that opens Quick Jump, e.g. "Ctrl+Alt+Space".</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    public bool StartWithWindows { get; set; }
    public bool ExplorerContextMenu { get; set; }

    /// <summary>Learn which folders you use from the File Explorer windows you open.</summary>
    public bool LearnFromExplorer { get; set; } = true;

    /// <summary>Folders searched by Quick Jump. Empty means the defaults (your user folder).</summary>
    public List<string> IndexRoots { get; set; } = [];
    public List<string> ExcludedFolderNames { get; set; } = [.. PathFilter.DefaultExcludedNames];
    public bool ExcludeDotFolders { get; set; } = true;
    public bool IncludeHiddenFiles { get; set; }

    /// <summary>Upper bound on indexed items, which caps the index's memory (about 50 bytes per item).</summary>
    public int MaxIndexedItems { get; set; } = 1_000_000;

    public int UnusedDays { get; set; } = 180;
    public OrganizeMode OrganizeMode { get; set; } = OrganizeMode.ByType;

    /// <summary>The user's own folder names by file type (".stl" → "3D Prints"), used when organizing by type.</summary>
    public Dictionary<string, string> OrganizeFolders { get; set; } = [];

    /// <summary>Every 15 minutes, sort finished downloads into folders.</summary>
    public bool AutoTidyDownloads { get; set; }

    /// <summary>Give memory back to Windows while Zoomies sits idle in the tray.</summary>
    public bool LowMemoryMode { get; set; } = true;

    /// <summary>Check GitHub once a day for a new version and install it on the next start (installed copies only).</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>"system" (Sand in light mode, Midnight in dark mode), "sand" or "midnight".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>"auto" follows Windows; otherwise a culture name such as "es" or "hi".</summary>
    public string Language { get; set; } = "auto";

    public bool HasCompletedWelcome { get; set; }

    public PathFilter CreateFilter() => new(ExcludedFolderNames, ExcludeDotFolders, IncludeHiddenFiles);

    public IReadOnlyList<string> EffectiveIndexRoots() =>
        IndexRoots.Count > 0
            ? PathUtil.RemoveNested(IndexRoots.Where(Directory.Exists))
            : KnownFolders.DefaultIndexRoots();
}
