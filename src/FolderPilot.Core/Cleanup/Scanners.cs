using System.Security.Cryptography;
using FolderPilot.Core.Platform;

namespace FolderPilot.Core.Cleanup;

/// <summary>A file suggested for cleanup. <see cref="LastUsedUtc"/> is the latest of opened, changed and arrived-on-this-PC.</summary>
public sealed record FileCandidate(string Path, long Size, DateTime LastUsedUtc);

public sealed record DuplicateGroup(long Size, List<FileCandidate> Files)
{
    public long WastedBytes => Size * (Files.Count - 1);
}

public static class UnusedFileScanner
{
    /// <summary>Files not opened or changed in <paramref name="minDaysUnused"/> days, biggest first.</summary>
    public static List<FileCandidate> Scan(IEnumerable<string> roots, PathFilter filter, int minDaysUnused, long minSize,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default, DateTime? nowUtc = null, int maxResults = 20_000)
    {
        bool useAccess = Shell.IsLastAccessTracked();
        DateTime cutoff = (nowUtc ?? DateTime.UtcNow).AddDays(-minDaysUnused);
        var results = new List<FileCandidate>();
        foreach (var f in FileWalker.Files(roots, filter, minSize, progress, ct))
        {
            DateTime lastUsed = LastUsed(f, useAccess);
            if (lastUsed <= cutoff) results.Add(new FileCandidate(f.Path, f.Length, lastUsed));
        }
        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (results.Count > maxResults) results.RemoveRange(maxResults, results.Count - maxResults);
        return results;
    }

    /// <summary>
    /// The creation time counts as "use" too: a file copied or downloaded last week is new to this PC
    /// even if its contents are old. Erring towards "recently used" means fewer wrong suggestions.
    /// </summary>
    public static DateTime LastUsed(WalkedFile f, bool useAccessTime)
    {
        DateTime last = f.LastWriteUtc > f.CreationUtc ? f.LastWriteUtc : f.CreationUtc;
        if (useAccessTime && f.LastAccessUtc > last) last = f.LastAccessUtc;
        return last;
    }
}

public static class LargeFileScanner
{
    public static List<FileCandidate> Scan(IEnumerable<string> roots, PathFilter filter, long minSize,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default, int maxResults = 5_000)
    {
        bool useAccess = Shell.IsLastAccessTracked();
        var results = FileWalker.Files(roots, filter, minSize, progress, ct)
            .Select(f => new FileCandidate(f.Path, f.Length, UnusedFileScanner.LastUsed(f, useAccess)))
            .ToList();
        results.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (results.Count > maxResults) results.RemoveRange(maxResults, results.Count - maxResults);
        return results;
    }
}

/// <summary>
/// Finds files with identical contents in three passes so most files are never read in full:
/// same size → same first and last 64 KB → same SHA-256 of the whole file.
/// </summary>
public static class DuplicateFinder
{
    private const int SampleSize = 64 * 1024;

    public static List<DuplicateGroup> Find(IEnumerable<string> roots, PathFilter filter, long minSize,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        bool useAccess = Shell.IsLastAccessTracked();
        var bySize = new Dictionary<long, List<FileCandidate>>();
        foreach (var f in FileWalker.Files(roots, filter, Math.Max(1, minSize), progress, ct))
        {
            if (!bySize.TryGetValue(f.Length, out var list)) bySize[f.Length] = list = [];
            list.Add(new FileCandidate(f.Path, f.Length, UnusedFileScanner.LastUsed(f, useAccess)));
        }

        var groups = new List<DuplicateGroup>();
        var buffer = new byte[SampleSize];
        int hashed = 0;
        foreach (var (size, sameSize) in bySize)
        {
            if (sameSize.Count < 2) continue;
            foreach (var sameSample in GroupBy(sameSize, f => SampleHash(f.Path, size, buffer)))
            {
                ct.ThrowIfCancellationRequested();
                var finalGroups = size <= SampleSize * 2
                    ? [sameSample] // the samples already covered the whole file
                    : GroupBy(sameSample, f => FullHash(f.Path, ct));
                foreach (var g in finalGroups)
                {
                    // Oldest first: that copy is the one suggested to keep.
                    g.Sort((a, b) => a.LastUsedUtc.CompareTo(b.LastUsedUtc));
                    groups.Add(new DuplicateGroup(size, g));
                }
                hashed += sameSample.Count;
                progress?.Report(new ScanProgress(hashed, Path.GetDirectoryName(sameSample[0].Path) ?? ""));
            }
        }
        groups.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
        return groups;
    }

    private static List<List<FileCandidate>> GroupBy(List<FileCandidate> files, Func<FileCandidate, string?> key)
    {
        var map = new Dictionary<string, List<FileCandidate>>();
        foreach (var f in files)
        {
            string? k = key(f);
            if (k is null) continue; // unreadable (in use, or removed meanwhile)
            if (!map.TryGetValue(k, out var list)) map[k] = list = [];
            list.Add(f);
        }
        return map.Values.Where(l => l.Count > 1).ToList();
    }

    private static string? SampleHash(string path, long size, byte[] buffer)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read = stream.ReadAtLeast(buffer, (int)Math.Min(SampleSize, size), throwOnEndOfStream: false);
            sha.AppendData(buffer, 0, read);
            if (size > SampleSize * 2)
            {
                stream.Seek(-SampleSize, SeekOrigin.End);
                read = stream.ReadAtLeast(buffer, SampleSize, throwOnEndOfStream: false);
                sha.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string? FullHash(string path, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

public static class EmptyFolderScanner
{
    private static readonly string[] Ignorable = ["desktop.ini", "thumbs.db", ".ds_store"];

    /// <summary>Folders that contain nothing (or only desktop.ini / thumbs.db), reported at the highest empty level.</summary>
    public static List<string> Scan(IEnumerable<string> roots, PathFilter filter, IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<string>();
        int scanned = 0;
        foreach (string root in PathUtil.RemoveNested(roots))
        {
            if (!Directory.Exists(root)) continue;
            // The roots themselves (Downloads, Documents…) are never suggested.
            foreach (string child in SafeDirectories(root))
            {
                if (filter.IsExcludedDirectory(root, Path.GetFileName(child.AsSpan()))) continue;
                if (IsEmpty(child, filter, results, 0, ref scanned, progress, ct)) results.Add(child);
            }
        }
        results.Sort(StringComparer.CurrentCultureIgnoreCase);
        return results;
    }

    private static bool IsEmpty(string dir, PathFilter filter, List<string> results, int depth, ref int scanned,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (++scanned % 200 == 0) progress?.Report(new ScanProgress(scanned, dir));
        if (depth > 40) return false;

        FileAttributes attributes;
        try { attributes = File.GetAttributes(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) return false;

        // Hidden files count as content: a folder holding only hidden files is not empty.
        var all = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
        bool hasContent = false;
        try
        {
            hasContent = Directory.EnumerateFiles(dir, "*", all)
                .Any(file => !Ignorable.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }

        // Keep going even when this folder has files: its subfolders may still be empty.
        var emptyChildren = new List<string>();
        foreach (string child in SafeDirectories(dir))
        {
            if (filter.IsExcludedDirectory(dir, Path.GetFileName(child.AsSpan()))) { hasContent = true; continue; }
            if (IsEmpty(child, filter, results, depth + 1, ref scanned, progress, ct)) emptyChildren.Add(child);
            else hasContent = true;
        }
        if (!hasContent) return true;
        results.AddRange(emptyChildren);
        return false;
    }

    private static List<string> SafeDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
