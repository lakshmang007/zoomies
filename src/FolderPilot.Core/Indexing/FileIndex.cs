using System.IO.Enumeration;

namespace FolderPilot.Core.Indexing;

public readonly record struct SearchHit(string Path, string Name, bool IsDirectory, double Score, DateTime ModifiedUtc);

public enum SearchScope { All, FoldersOnly, FilesOnly }

/// <summary>
/// In-memory index of file and folder names, built for low memory use. Entries are stored as parallel arrays
/// (parent, name offset, name length, flags, modified time = 15 bytes) plus the name characters in one shared
/// buffer. There are no per-file objects, so 100,000 files take about 5 MB instead of 30+ MB.
/// </summary>
public sealed class FileIndex
{
    private const byte FlagDirectory = 1;
    private const byte FlagDeleted = 2;
    private const int MaxDepth = 48;
    private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly object _gate = new();
    private readonly PathFilter _filter;
    private readonly int _maxEntries;
    private int _count, _deleted, _charCount;
    private bool _trimmed;
    private int[] _parent, _nameStart, _modified;
    private ushort[] _nameLen;
    private byte[] _flags;
    private char[] _chars;

    public FileIndex(PathFilter? filter = null, int maxEntries = 1_000_000, int capacity = 4096)
    {
        _filter = filter ?? PathFilter.Default;
        _maxEntries = maxEntries;
        _parent = new int[capacity];
        _nameStart = new int[capacity];
        _modified = new int[capacity];
        _nameLen = new ushort[capacity];
        _flags = new byte[capacity];
        _chars = new char[capacity * 16];
    }

    public int Count { get { lock (_gate) return _count - _deleted; } }
    public int DeletedCount { get { lock (_gate) return _deleted; } }

    /// <summary>Bytes held by the index arrays.</summary>
    public long ApproximateBytes
    {
        get { lock (_gate) return (long)_parent.Length * 15 + (long)_chars.Length * sizeof(char); }
    }

    // ───────────────────────────── building ─────────────────────────────

    /// <summary>Walks the roots and indexes every file and folder the filter allows.</summary>
    public static FileIndex Build(IReadOnlyList<string> roots, PathFilter filter, int maxEntries = 1_000_000,
        Action<int>? progress = null, CancellationToken ct = default)
    {
        var index = new FileIndex(filter, maxEntries, 1 << 15);
        foreach (string root in PathUtil.RemoveNested(roots))
        {
            if (!Directory.Exists(root)) continue;
            int rootIdx = index.AddUnsafe(-1, root.AsSpan(), true, SafeLastWrite(root));
            index.ScanUnsafe(root, rootIdx, 0, progress, ct);
        }
        index.TrimExcess();
        return index;
    }

    /// <summary>Depth-first walk below <paramref name="dir"/>. Caller holds the lock (or owns the index exclusively).</summary>
    private void ScanUnsafe(string dir, int dirIdx, int depth, Action<int>? progress, CancellationToken ct)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = _filter.AttributesToSkip,
            ReturnSpecialDirectories = false,
        };
        var stack = new Stack<(string Path, int Index, int Depth)>();
        stack.Push((dir, dirIdx, depth));
        int lastReport = _count;

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, parentIdx, level) = stack.Pop();
            try
            {
                // Folder links and online-only cloud folders are indexed but not walked into: links can loop or
                // lead outside the roots, and listing an online-only folder makes Windows download it.
                var entries = new FileSystemEnumerable<int>(current,
                    (ref FileSystemEntry e) =>
                    {
                        int idx = AddUnsafe(parentIdx, e.FileName, e.IsDirectory, e.LastWriteTimeUtc.UtcDateTime);
                        bool walk = e.IsDirectory && !CloudFiles.IsOnlineOnly(e.Attributes) && !Links.IsLink(ref e);
                        return walk || !e.IsDirectory ? idx : ~idx;
                    },
                    options)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) =>
                        !e.IsDirectory || !_filter.IsExcludedDirectory(e.Directory, e.FileName),
                };
                foreach (int result in entries)
                {
                    if (result < 0) continue; // a folder we don't walk into
                    int idx = result;
                    if ((_flags[idx] & FlagDirectory) != 0 && level < MaxDepth)
                        stack.Push((Path.Join(current.AsSpan(), NameSpan(idx)), idx, level + 1));
                    if (_count >= _maxEntries) return;
                }
            }
            // Folders can vanish mid-scan or be virtual (phone links, disconnected drives); skip and carry on.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            if (progress is not null && _count - lastReport >= 4096)
            {
                progress(_count);
                lastReport = _count;
            }
        }
    }

    private int AddUnsafe(int parent, ReadOnlySpan<char> name, bool isDirectory, DateTime modifiedUtc)
    {
        if (_count == _parent.Length) GrowEntries();
        if (_charCount + name.Length > _chars.Length) GrowChars(name.Length);

        int i = _count++;
        _parent[i] = parent;
        _nameStart[i] = _charCount;
        _nameLen[i] = (ushort)Math.Min(name.Length, ushort.MaxValue);
        name[.._nameLen[i]].CopyTo(_chars.AsSpan(_charCount));
        _charCount += _nameLen[i];
        _flags[i] = isDirectory ? FlagDirectory : (byte)0;
        _modified[i] = ToStamp(modifiedUtc);
        return i;
    }

    private void GrowEntries()
    {
        // Double while building; grow gently once trimmed so one new file does not double the arrays.
        int size = _trimmed ? _parent.Length + Math.Max(1024, _parent.Length / 8) : Math.Max(1024, _parent.Length * 2);
        Array.Resize(ref _parent, size);
        Array.Resize(ref _nameStart, size);
        Array.Resize(ref _modified, size);
        Array.Resize(ref _nameLen, size);
        Array.Resize(ref _flags, size);
    }

    private void GrowChars(int needed)
    {
        int size = _trimmed ? _chars.Length + Math.Max(16384, _chars.Length / 8) : _chars.Length * 2;
        Array.Resize(ref _chars, Math.Max(size, _charCount + needed));
    }

    /// <summary>Releases the spare capacity left over from building.</summary>
    public void TrimExcess()
    {
        lock (_gate)
        {
            int size = Math.Max(_count, 1);
            Array.Resize(ref _parent, size);
            Array.Resize(ref _nameStart, size);
            Array.Resize(ref _modified, size);
            Array.Resize(ref _nameLen, size);
            Array.Resize(ref _flags, size);
            Array.Resize(ref _chars, Math.Max(_charCount, 1));
            _trimmed = true;
        }
    }

    // ───────────────────────────── searching ─────────────────────────────

    /// <summary>
    /// Finds entries whose name matches the last word typed. Any other words must appear in the name or in a
    /// parent folder's name, so "college notes" finds "Documents\College\notes.docx".
    /// </summary>
    /// <param name="boost">Optional extra score per path, e.g. from how often the user visits it.</param>
    public List<SearchHit> Search(string query, int maxResults = 30, SearchScope scope = SearchScope.All,
        Func<string, double>? boost = null, CancellationToken ct = default)
    {
        string[] tokens = TextMatch.Tokenize(query);
        if (tokens.Length == 0 || maxResults <= 0) return [];
        string last = tokens[^1];
        int pool = Math.Max(maxResults * 4, 120);
        var best = new PriorityQueue<int, double>(pool + 1);
        int nowStamp = ToStamp(DateTime.UtcNow);
        var hits = new List<SearchHit>(pool);

        lock (_gate)
        {
            for (int i = 0; i < _count; i++)
            {
                if ((i & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                byte flags = _flags[i];
                if ((flags & FlagDeleted) != 0) continue;
                bool isDir = (flags & FlagDirectory) != 0;
                if ((scope == SearchScope.FoldersOnly && !isDir) || (scope == SearchScope.FilesOnly && isDir)) continue;

                ReadOnlySpan<char> name = DisplayNameSpan(i);
                double score = TextMatch.Score(name, last);
                if (score <= 0) continue;
                if (tokens.Length > 1 && !MatchOtherTokens(i, name, tokens, ref score)) continue;
                score += Adjustments(i, isDir, nowStamp);

                if (best.Count < pool) best.Enqueue(i, score);
                else if (best.TryPeek(out _, out double lowest) && score > lowest) best.EnqueueDequeue(i, score);
            }

            while (best.TryDequeue(out int idx, out double score))
            {
                if (HasDeletedAncestor(idx)) continue;
                hits.Add(new SearchHit(GetPathUnsafe(idx), DisplayNameSpan(idx).ToString(),
                    (_flags[idx] & FlagDirectory) != 0, score, FromStamp(_modified[idx])));
            }
        }

        if (boost is not null)
            for (int i = 0; i < hits.Count; i++)
                hits[i] = hits[i] with { Score = hits[i].Score + boost(hits[i].Path) };

        hits.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.Path.Length.CompareTo(b.Path.Length));
        if (hits.Count > maxResults) hits.RemoveRange(maxResults, hits.Count - maxResults);
        return hits;
    }

    private bool MatchOtherTokens(int i, ReadOnlySpan<char> name, string[] tokens, ref double score)
    {
        for (int t = 0; t < tokens.Length - 1; t++)
        {
            string token = tokens[t];
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase)) { score += 12; continue; }
            bool found = false;
            for (int p = _parent[i]; p >= 0; p = _parent[p])
            {
                if (DisplayNameSpan(p).Contains(token, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            }
            if (!found) return false;
            score += 6;
        }
        return true;
    }

    private double Adjustments(int i, bool isDir, int nowStamp)
    {
        double adjust = isDir ? 4 : 0;
        int depth = 0;
        for (int p = _parent[i]; p >= 0 && depth < 16; p = _parent[p]) depth++;
        adjust -= Math.Min(8, depth * 0.6);

        int ageMinutes = nowStamp - _modified[i];
        if (ageMinutes < 60 * 24) adjust += 6;
        else if (ageMinutes < 60 * 24 * 7) adjust += 4;
        else if (ageMinutes < 60 * 24 * 30) adjust += 2;
        return adjust;
    }

    // ───────────────────────────── live updates ─────────────────────────────

    /// <summary>Adds a new file or folder (and, for a folder, everything inside it). Returns false if its parent isn't indexed.</summary>
    public bool ApplyCreated(string fullPath)
    {
        string path = Path.TrimEndingDirectorySeparator(fullPath);
        string? parentPath = Path.GetDirectoryName(path);
        if (parentPath is null) return false;
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) return false;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & _filter.AttributesToSkip) != 0) return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (isDir && _filter.IsExcludedDirectory(parentPath, Path.GetFileName(path.AsSpan()))) return false;

        lock (_gate)
        {
            if (_count >= _maxEntries || FindUnsafe(path) >= 0) return false;
            int parent = FindUnsafe(parentPath);
            if (parent < 0) return false;
            int idx = AddUnsafe(parent, Path.GetFileName(path.AsSpan()), isDir, SafeLastWrite(path));
            // A folder moved in from elsewhere arrives as a single "created" event, so index its contents too.
            if (isDir) ScanUnsafe(path, idx, 1, null, CancellationToken.None);
            return true;
        }
    }

    public bool ApplyDeleted(string fullPath)
    {
        lock (_gate)
        {
            int idx = FindUnsafe(Path.TrimEndingDirectorySeparator(fullPath));
            if (idx < 0) return false;
            // Children are hidden through their deleted ancestor, so one flag covers a whole folder tree.
            _flags[idx] |= FlagDeleted;
            _deleted++;
            return true;
        }
    }

    public void ApplyRenamed(string oldPath, string newPath)
    {
        ApplyDeleted(oldPath);
        ApplyCreated(newPath);
    }

    public bool Contains(string fullPath)
    {
        lock (_gate) return FindUnsafe(Path.TrimEndingDirectorySeparator(fullPath)) >= 0;
    }

    private int FindUnsafe(string path)
    {
        ReadOnlySpan<char> name = Path.GetFileName(path.AsSpan());
        for (int i = 0; i < _count; i++)
        {
            if ((_flags[i] & FlagDeleted) != 0) continue;
            ReadOnlySpan<char> stored = NameSpan(i);
            if (_parent[i] < 0)
            {
                if (stored.Equals(path, StringComparison.OrdinalIgnoreCase)) return i;
            }
            else if (stored.Equals(name, StringComparison.OrdinalIgnoreCase)
                     && !HasDeletedAncestor(i)
                     && GetPathUnsafe(i).Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private ReadOnlySpan<char> NameSpan(int i) => _chars.AsSpan(_nameStart[i], _nameLen[i]);

    /// <summary>Roots store their full path; show just the folder name for them.</summary>
    private ReadOnlySpan<char> DisplayNameSpan(int i)
    {
        ReadOnlySpan<char> name = NameSpan(i);
        if (_parent[i] >= 0) return name;
        ReadOnlySpan<char> leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(name));
        return leaf.IsEmpty ? name : leaf;
    }

    private bool HasDeletedAncestor(int i)
    {
        for (int p = i; p >= 0; p = _parent[p])
            if ((_flags[p] & FlagDeleted) != 0) return true;
        return false;
    }

    private string GetPathUnsafe(int i)
    {
        int depth = 0, length = 0;
        for (int p = i; p >= 0; p = _parent[p])
        {
            depth++;
            length += _nameLen[p] + 1;
        }

        Span<int> chain = depth <= 128 ? stackalloc int[depth] : new int[depth];
        int n = 0;
        for (int p = i; p >= 0; p = _parent[p]) chain[n++] = p;

        Span<char> buffer = length <= 512 ? stackalloc char[length] : new char[length];
        int pos = 0;
        for (int d = depth - 1; d >= 0; d--)
        {
            ReadOnlySpan<char> part = NameSpan(chain[d]);
            if (pos > 0 && buffer[pos - 1] != '\\') buffer[pos++] = '\\';
            part.CopyTo(buffer[pos..]);
            pos += part.Length;
        }
        return new string(buffer[..pos]);
    }

    private static DateTime SafeLastWrite(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Epoch; }
    }

    private static int ToStamp(DateTime utc)
    {
        double minutes = (utc - Epoch).TotalMinutes;
        return minutes <= 0 ? 0 : minutes >= int.MaxValue ? int.MaxValue : (int)minutes;
    }

    private static DateTime FromStamp(int stamp) => Epoch.AddMinutes(stamp);
}
