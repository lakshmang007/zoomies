using System.Globalization;

namespace FolderPilot.Core.Organize;

public enum OrganizeMode
{
    /// <summary>Documents\, Images\, Installers\…</summary>
    ByType,
    /// <summary>Images\2026-10\…</summary>
    ByTypeAndMonth,
    /// <summary>2026-10\…</summary>
    ByMonth,
}

public sealed class OrganizeOptions
{
    public OrganizeMode Mode { get; set; } = OrganizeMode.ByType;
    /// <summary>Leave .lnk/.url shortcuts where they are (mainly for the Desktop).</summary>
    public bool KeepShortcuts { get; set; } = true;
    /// <summary>Files changed this recently may still be downloading or open, so they are left alone.</summary>
    public int SkipRecentMinutes { get; set; } = 10;
    public bool IncludeHidden { get; set; }

    /// <summary>
    /// The user's own folder names by file type, e.g. ".stl" → "3D Prints". Keys are lower-case extensions with the dot
    /// ("" for files without one). Used by the type-based modes instead of the category folder.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CustomFolders { get; set; }
}

public sealed record PlannedMove(string Source, string Destination, FileCategory Category, long Size, DateTime LastWriteUtc)
{
    public string Extension => OrganizePlanner.ExtensionKey(Source);
}

public sealed class OrganizePlan
{
    public required string Folder { get; init; }
    public required List<PlannedMove> Moves { get; init; }
    public int Skipped { get; init; }

    public long TotalBytes => Moves.Sum(m => m.Size);
    public int FolderCount => Moves.Select(m => Path.GetDirectoryName(m.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}

/// <summary>Works out where each loose file in a folder should go. Nothing is touched until the plan is executed.</summary>
public static class OrganizePlanner
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <param name="folderName">Name for a category's subfolder. Lets the app use translated names ("Imágenes").</param>
    public static OrganizePlan Plan(string folder, OrganizeOptions options, Func<FileCategory, string>? folderName = null,
        DateTime? nowUtc = null)
    {
        folderName ??= c => c.ToString();
        DateTime cutoff = (nowUtc ?? DateTime.UtcNow).AddMinutes(-options.SkipRecentMinutes);
        var moves = new List<PlannedMove>();
        int skipped = 0;

        var enumeration = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = options.IncludeHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System,
        };

        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", enumeration))
        {
            if (FileCategories.IsTemporary(file.Name)
                || (options.KeepShortcuts && FileCategories.IsShortcut(file.Name))
                || file.LastWriteTimeUtc > cutoff)
            {
                skipped++;
                continue;
            }

            var category = FileCategories.Of(file.Name);
            string destination = Destination(folder, file.Name, category, file.LastWriteTimeUtc, options, folderName);
            moves.Add(new PlannedMove(file.FullName, destination, category, file.Length, file.LastWriteTimeUtc));
        }

        Sort(moves);
        return new OrganizePlan { Folder = folder, Moves = moves, Skipped = skipped };
    }

    /// <summary>
    /// Same files, new destinations, e.g. after the user renamed a folder in the preview.
    /// Nothing is read from disk, so the preview updates instantly.
    /// </summary>
    public static OrganizePlan Retarget(OrganizePlan plan, OrganizeOptions options, Func<FileCategory, string>? folderName = null)
    {
        folderName ??= c => c.ToString();
        var moves = plan.Moves
            .Select(m => m with
            {
                Destination = Destination(plan.Folder, Path.GetFileName(m.Source), m.Category, m.LastWriteUtc, options, folderName),
            })
            .ToList();
        Sort(moves);
        return new OrganizePlan { Folder = plan.Folder, Moves = moves, Skipped = plan.Skipped };
    }

    /// <summary>The subfolder (relative to the organized folder) a file type goes to in the type-based modes.</summary>
    public static string TypeFolder(string extensionKey, FileCategory category, OrganizeOptions options, Func<FileCategory, string> folderName) =>
        options.CustomFolders is { } custom && custom.TryGetValue(extensionKey, out string? name) && IsValidFolderName(name)
            ? name.Trim()
            : folderName(category);

    /// <summary>".stl" for "part.STL"; "" for files without an extension.</summary>
    public static string ExtensionKey(string fileName) => Path.GetExtension(fileName).ToLowerInvariant();

    /// <summary>
    /// A single folder name Windows accepts: not empty, no \ / : * ? " &lt; &gt; |, not "." or "..",
    /// no trailing dot, and not a reserved device name like CON or NUL.
    /// </summary>
    public static bool IsValidFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (name.Length > 120 || name is "." or ".." || name.EndsWith('.')) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        string stem = name.Split('.')[0];
        return !ReservedNames.Contains(stem);
    }

    private static string Destination(string folder, string fileName, FileCategory category, DateTime lastWriteUtc,
        OrganizeOptions options, Func<FileCategory, string> folderName)
    {
        string month = lastWriteUtc.ToLocalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        string typeFolder = TypeFolder(ExtensionKey(fileName), category, options, folderName);
        string targetDir = options.Mode switch
        {
            OrganizeMode.ByType => Path.Combine(folder, typeFolder),
            OrganizeMode.ByTypeAndMonth => Path.Combine(folder, typeFolder, month),
            _ => Path.Combine(folder, month),
        };
        return Path.Combine(targetDir, fileName);
    }

    /// <summary>Grouped by destination folder so the preview reads folder by folder.</summary>
    private static void Sort(List<PlannedMove> moves) =>
        moves.Sort((a, b) =>
        {
            int byFolder = string.Compare(Path.GetDirectoryName(a.Destination), Path.GetDirectoryName(b.Destination),
                StringComparison.CurrentCultureIgnoreCase);
            return byFolder != 0
                ? byFolder
                : string.Compare(Path.GetFileName(a.Source), Path.GetFileName(b.Source), StringComparison.CurrentCultureIgnoreCase);
        });
}
