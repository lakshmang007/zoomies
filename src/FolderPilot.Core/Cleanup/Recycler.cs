using FolderPilot.Core.History;
using FolderPilot.Core.Platform;

namespace FolderPilot.Core.Cleanup;

public readonly record struct RecycleOutcome(int Recycled, int Skipped, long Bytes, JournalEntry? Entry);

public static class Recycler
{
    /// <summary>Sends files or folders to the Recycle Bin and records them so Undo can put them back.</summary>
    public static RecycleOutcome Recycle(IReadOnlyList<(string Path, long Size)> items, OperationJournal? journal,
        string title, IntPtr ownerWindow = default)
    {
        var entry = new JournalEntry { Kind = JournalKind.Recycle, Title = title, TimeUtc = DateTime.UtcNow };
        var sizes = items.GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Size, StringComparer.OrdinalIgnoreCase);
        var dirs = items.Where(i => Directory.Exists(i.Path)).Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recycled = RecycleBin.Send(sizes.Keys.ToList(), ownerWindow);
        long bytes = 0;
        foreach (string path in recycled)
        {
            long size = sizes[path];
            bytes += size;
            entry.Ops.Add(new FileOp { From = path, Size = size, IsDirectory = dirs.Contains(path) });
        }
        journal?.Add(entry);
        return new RecycleOutcome(recycled.Count, sizes.Count - recycled.Count, bytes, entry.Ops.Count > 0 ? entry : null);
    }
}
