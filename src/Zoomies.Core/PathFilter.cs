namespace Zoomies.Core;

/// <summary>Decides which folders Zoomies walks into. Shared by the search index and the cleanup scanners.</summary>
public sealed class PathFilter
{
    public static readonly IReadOnlyList<string> DefaultExcludedNames =
    [
        "AppData", "node_modules", "bower_components", "__pycache__", "venv", "site-packages",
        "$RECYCLE.BIN", "System Volume Information",
        // Windows Phone Link's live view of a phone: listing it goes over the network to the phone.
        "CrossDevice",
    ];

    /// <summary>Operating-system folders, skipped only when they sit directly on a drive (C:\Windows), never inside user folders.</summary>
    private static readonly string[] DriveRootSystemFolders =
        ["Windows", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "PerfLogs", "$WinREAgent"];

    private readonly HashSet<string> _names;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public PathFilter(IEnumerable<string>? excludedNames = null, bool excludeDotFolders = true, bool includeHidden = false)
    {
        _names = new HashSet<string>(
            (excludedNames ?? DefaultExcludedNames).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);
        _lookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
        ExcludeDotFolders = excludeDotFolders;
        IncludeHidden = includeHidden;
    }

    public static PathFilter Default { get; } = new();

    public bool ExcludeDotFolders { get; }
    public bool IncludeHidden { get; }

    /// <summary>System files are never touched; hidden ones only when the user opts in.</summary>
    public FileAttributes AttributesToSkip =>
        IncludeHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System;

    public bool IsExcludedDirectoryName(ReadOnlySpan<char> name) =>
        (ExcludeDotFolders && name.Length > 0 && name[0] == '.') || _lookup.Contains(name);

    /// <summary>Full check for a folder named <paramref name="name"/> found inside <paramref name="parentDirectory"/>.</summary>
    public bool IsExcludedDirectory(ReadOnlySpan<char> parentDirectory, ReadOnlySpan<char> name)
    {
        if (IsExcludedDirectoryName(name)) return true;
        if (!IsDriveRoot(parentDirectory)) return false;
        foreach (string system in DriveRootSystemFolders)
            if (name.Equals(system, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>True when any folder in the path (below the drive, or below <paramref name="root"/> if given) is excluded.</summary>
    public bool IsExcludedPath(string fullPath, string? root = null)
    {
        string? dir = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (root is not null && dir.Length <= root.Length) break; // the root itself is always allowed
            string? parent = Path.GetDirectoryName(dir);
            if (parent is null) break; // reached the drive root
            if (IsExcludedDirectory(parent, Path.GetFileName(dir.AsSpan()))) return true;
            dir = parent;
        }
        return false;
    }

    private static bool IsDriveRoot(ReadOnlySpan<char> dir) =>
        (dir.Length == 2 && dir[1] == ':') || (dir.Length == 3 && dir[1] == ':' && dir[2] is '\\' or '/');
}

/// <summary>OneDrive and other cloud providers leave "online-only" placeholders on disk. Reading one downloads it, and it uses no local space, so scanners skip them.</summary>
public static class CloudFiles
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static bool IsOnlineOnly(FileAttributes attributes) =>
        (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
}

public static class Links
{
    /// <summary>
    /// True for symbolic links and junctions, which can loop back or point outside the folder being scanned.
    /// False for OneDrive/cloud folders, which are reparse points too but hold real content.
    /// </summary>
    public static bool IsLink(ref System.IO.Enumeration.FileSystemEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return false;
        try
        {
            FileSystemInfo info = entry.IsDirectory ? new DirectoryInfo(entry.ToFullPath()) : new FileInfo(entry.ToFullPath());
            return info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }
}
