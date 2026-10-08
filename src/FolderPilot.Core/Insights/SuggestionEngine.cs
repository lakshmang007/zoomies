using FolderPilot.Core.Cleanup;
using FolderPilot.Core.Organize;

namespace FolderPilot.Core.Insights;

public enum SuggestionKind { OrganizeFolder, OldInstallers, UnusedFiles, LargeFiles }

public sealed record Suggestion(SuggestionKind Kind, string Path, int Count, long Bytes);

/// <summary>Looks around the user's main folders for easy wins. Quick checks first, the slower full scan last.</summary>
public static class SuggestionEngine
{
    public const int InstallerAgeDays = 14;

    /// <summary>Checks that only list a couple of folders (well under a second).</summary>
    public static List<Suggestion> Quick(string downloads, string desktop, DateTime? nowUtc = null)
    {
        var list = new List<Suggestion>();
        DateTime now = nowUtc ?? DateTime.UtcNow;

        foreach (var (folder, threshold) in new[] { (downloads, 8), (desktop, 12) })
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var plan = OrganizePlanner.Plan(folder, new OrganizeOptions(), nowUtc: now);
                if (plan.Moves.Count >= threshold)
                    list.Add(new Suggestion(SuggestionKind.OrganizeFolder, folder, plan.Moves.Count, plan.TotalBytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var installers = OldInstallers(downloads, now);
        if (installers.Count > 0)
            list.Add(new Suggestion(SuggestionKind.OldInstallers, downloads, installers.Count, installers.Sum(f => f.Size)));
        return list;
    }

    /// <summary>Setup files in Downloads (and the "Installers" folder the organizer makes) older than two weeks: once installed, they are rarely needed again.</summary>
    public static List<FileCandidate> OldInstallers(string downloads, DateTime? nowUtc = null)
    {
        DateTime cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-InstallerAgeDays);
        var results = new List<FileCandidate>();
        if (!Directory.Exists(downloads)) return results;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
        var folders = new List<string> { downloads };
        try
        {
            folders.AddRange(Directory.EnumerateDirectories(downloads, "*", options)
                .Where(d => Path.GetFileName(d).Contains("install", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (string folder in folders)
        {
            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
                {
                    if (!FileCategories.IsInstaller(file.Name) || CloudFiles.IsOnlineOnly(file.Attributes)) continue;
                    DateTime arrived = file.CreationTimeUtc > file.LastWriteTimeUtc ? file.CreationTimeUtc : file.LastWriteTimeUtc;
                    if (arrived < cutoff) results.Add(new FileCandidate(file.FullName, file.Length, arrived));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        return results;
    }

    /// <summary>Full scan of the personal folders for files unused for <paramref name="unusedDays"/> days.</summary>
    public static Suggestion? Unused(IReadOnlyList<string> folders, PathFilter filter, int unusedDays, CancellationToken ct)
    {
        var files = UnusedFileScanner.Scan(folders, filter, unusedDays, 1024 * 1024, ct: ct);
        long bytes = files.Sum(f => f.Size);
        return bytes >= 100L * 1024 * 1024
            ? new Suggestion(SuggestionKind.UnusedFiles, "", files.Count, bytes)
            : null;
    }
}
