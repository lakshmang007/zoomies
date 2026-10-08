using System.Text.Json.Serialization;
using FolderPilot.Core.Platform;
using FolderPilot.Core.Storage;

namespace FolderPilot.Core.History;

public enum JournalKind { Move, Recycle }

/// <summary>One thing FolderPilot moved or recycled.</summary>
public sealed class FileOp
{
    public string From { get; set; } = "";
    /// <summary>Where the item went. Empty for items sent to the Recycle Bin.</summary>
    public string To { get; set; } = "";
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
}

/// <summary>One user-visible action ("Organized Downloads") made of many file operations.</summary>
public sealed class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public JournalKind Kind { get; set; }
    public string Title { get; set; } = "";
    public List<FileOp> Ops { get; set; } = [];
    /// <summary>Folders the action created; removed again on undo if they end up empty.</summary>
    public List<string> CreatedDirectories { get; set; } = [];
    public bool Undone { get; set; }

    [JsonIgnore]
    public long TotalBytes => Ops.Sum(o => o.Size);
}

public readonly record struct UndoResult(int Restored, int Failed);

/// <summary>Remembers every change FolderPilot makes so any of them can be undone later.</summary>
public sealed class OperationJournal
{
    private const int MaxEntries = 300;
    private readonly object _gate = new();
    private readonly string _file;
    private List<JournalEntry> _entries;

    public OperationJournal(string? file = null)
    {
        _file = file ?? AppPaths.DataFile("history.json");
        _entries = JsonFile.Read(_file, CoreJsonContext.Default.ListJournalEntry) ?? [];
    }

    public event Action? Changed;

    public IReadOnlyList<JournalEntry> Entries
    {
        get { lock (_gate) return _entries.OrderByDescending(e => e.TimeUtc).ToList(); }
    }

    public JournalEntry? LastUndoable
    {
        get { lock (_gate) return _entries.Where(e => !e.Undone && e.Ops.Count > 0).MaxBy(e => e.TimeUtc); }
    }

    public void Add(JournalEntry entry)
    {
        if (entry.Ops.Count == 0) return;
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries = _entries.OrderByDescending(e => e.TimeUtc).Take(MaxEntries).ToList();
            Save();
        }
        Changed?.Invoke();
    }

    public UndoResult Undo(Guid id)
    {
        JournalEntry? entry;
        lock (_gate) entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry is null || entry.Undone) return new UndoResult(0, 0);

        var result = entry.Kind == JournalKind.Move ? UndoMoves(entry) : UndoRecycle(entry);
        lock (_gate)
        {
            entry.Undone = result.Failed == 0 || result.Restored > 0;
            Save();
        }
        Changed?.Invoke();
        return result;
    }

    private static UndoResult UndoMoves(JournalEntry entry)
    {
        int restored = 0, failed = 0;
        for (int i = entry.Ops.Count - 1; i >= 0; i--)
        {
            FileOp op = entry.Ops[i];
            try
            {
                bool present = op.IsDirectory ? Directory.Exists(op.To) : File.Exists(op.To);
                if (!present) { failed++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(op.From)!);
                string target = PathUtil.UniquePath(op.From);
                if (op.IsDirectory) Directory.Move(op.To, target);
                else File.Move(op.To, target);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        // Remove the category folders the action created, deepest first, if they are now empty.
        foreach (string dir in entry.CreatedDirectories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new UndoResult(restored, failed);
    }

    private static UndoResult UndoRecycle(JournalEntry entry)
    {
        int restored = 0, failed = 0;
        // Allow for clock granularity between when the entry was stamped and when Windows recorded the deletion.
        DateTime deletedAfter = entry.TimeUtc.AddMinutes(-1);
        foreach (FileOp op in entry.Ops)
        {
            if (RecycleBin.TryRestore(op.From, deletedAfter, out _)) restored++;
            else failed++;
        }
        return new UndoResult(restored, failed);
    }

    private void Save()
    {
        try { JsonFile.Write(_file, _entries, CoreJsonContext.Default.ListJournalEntry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
