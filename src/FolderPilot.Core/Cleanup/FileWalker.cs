using System.IO.Enumeration;

namespace FolderPilot.Core.Cleanup;

public readonly record struct WalkedFile(
    string Path, long Length, DateTime CreationUtc, DateTime LastWriteUtc, DateTime LastAccessUtc);

/// <summary>Progress for long scans: how many items were looked at and where the scan is now.</summary>
public readonly record struct ScanProgress(int ItemsScanned, string CurrentFolder);

/// <summary>Fast recursive file listing that reads sizes and dates straight from the directory listing (no extra disk calls per file).</summary>
public static class FileWalker
{
    public static IEnumerable<WalkedFile> Files(IEnumerable<string> roots, PathFilter filter, long minSize,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = filter.AttributesToSkip,
            MaxRecursionDepth = 48,
        };
        var counter = new ProgressCounter(progress);

        foreach (string root in PathUtil.RemoveNested(roots))
        {
            if (!Directory.Exists(root)) continue;
            FileSystemEnumerable<WalkedFile> files;
            try
            {
                files = new FileSystemEnumerable<WalkedFile>(root,
                    (ref FileSystemEntry e) => new WalkedFile(e.ToFullPath(), e.Length, e.CreationTimeUtc.UtcDateTime,
                        e.LastWriteTimeUtc.UtcDateTime, e.LastAccessTimeUtc.UtcDateTime),
                    options)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    {
                        counter.Tick(ref e);
                        if (e.IsDirectory || e.Length < minSize || CloudFiles.IsOnlineOnly(e.Attributes)) return false;
                        // A file symlink is counted where its real file lives, never twice.
                        return !Links.IsLink(ref e);
                    },
                    // Online-only cloud folders are skipped: listing them makes Windows download them.
                    ShouldRecursePredicate = (ref FileSystemEntry e) =>
                        !filter.IsExcludedDirectory(e.Directory, e.FileName)
                        && !CloudFiles.IsOnlineOnly(e.Attributes) && !Links.IsLink(ref e),
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            using var enumerator = files.GetEnumerator();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                bool hasNext;
                try { hasNext = enumerator.MoveNext(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { break; }
                if (!hasNext) break;
                yield return enumerator.Current;
            }
        }
        counter.Flush();
    }

    private sealed class ProgressCounter(IProgress<ScanProgress>? progress)
    {
        private int _count;
        private long _nextReport = Environment.TickCount64;

        public void Tick(ref FileSystemEntry entry)
        {
            _count++;
            if (progress is null || Environment.TickCount64 < _nextReport) return;
            _nextReport = Environment.TickCount64 + 150;
            progress.Report(new ScanProgress(_count, entry.Directory.ToString()));
        }

        public void Flush() => progress?.Report(new ScanProgress(_count, ""));
    }
}
