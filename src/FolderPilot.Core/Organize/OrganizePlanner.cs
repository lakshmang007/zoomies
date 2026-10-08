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
}

public sealed record PlannedMove(string Source, string Destination, FileCategory Category, long Size);

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
            string month = file.LastWriteTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            string targetDir = options.Mode switch
            {
                OrganizeMode.ByType => Path.Combine(folder, folderName(category)),
                OrganizeMode.ByTypeAndMonth => Path.Combine(folder, folderName(category), month),
                _ => Path.Combine(folder, month),
            };
            moves.Add(new PlannedMove(file.FullName, Path.Combine(targetDir, file.Name), category, file.Length));
        }

        moves.Sort((a, b) => a.Category != b.Category
            ? a.Category.CompareTo(b.Category)
            : string.Compare(Path.GetFileName(a.Source), Path.GetFileName(b.Source), StringComparison.CurrentCultureIgnoreCase));
        return new OrganizePlan { Folder = folder, Moves = moves, Skipped = skipped };
    }
}
