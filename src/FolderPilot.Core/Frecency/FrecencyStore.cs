using FolderPilot.Core.Indexing;
using FolderPilot.Core.Storage;

namespace FolderPilot.Core.Frecency;

public sealed class FrecencyEntry
{
    public double Rank { get; set; }
    /// <summary>Unix seconds of the last visit.</summary>
    public long Last { get; set; }
}

/// <summary>
/// Learns which folders you use. Score = how often × how recently (the "frecency" idea from browsers and zoxide),
/// so this week's project outranks a folder you opened a lot last year.
/// </summary>
public sealed class FrecencyStore
{
    private const double MaxTotalRank = 2000;
    private readonly object _gate = new();
    private readonly string _file;
    private Dictionary<string, FrecencyEntry> _map;
    private bool _dirty;

    public FrecencyStore(string? file = null)
    {
        _file = file ?? AppPaths.DataFile("folders.json");
        var loaded = JsonFile.Read(_file, CoreJsonContext.Default.DictionaryStringFrecencyEntry);
        _map = new Dictionary<string, FrecencyEntry>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public int Count { get { lock (_gate) return _map.Count; } }

    public void Visit(string path, double weight = 1, DateTime? whenUtc = null)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        long when = new DateTimeOffset(whenUtc ?? DateTime.UtcNow).ToUnixTimeSeconds();
        lock (_gate)
        {
            if (!_map.TryGetValue(path, out var entry)) _map[path] = entry = new FrecencyEntry();
            entry.Rank += weight;
            entry.Last = Math.Max(entry.Last, when);
            Age();
            _dirty = true;
        }
    }

    public void Remove(string path)
    {
        lock (_gate) _dirty |= _map.Remove(Path.TrimEndingDirectorySeparator(path));
    }

    public double Score(string path, DateTime? nowUtc = null)
    {
        lock (_gate)
        {
            return _map.TryGetValue(Path.TrimEndingDirectorySeparator(path), out var entry)
                ? Frecency(entry, ToUnix(nowUtc))
                : 0;
        }
    }

    public IReadOnlyList<(string Path, double Score)> Top(int count, DateTime? nowUtc = null)
    {
        long now = ToUnix(nowUtc);
        lock (_gate)
        {
            return _map.Select(kv => (kv.Key, Frecency(kv.Value, now)))
                .OrderByDescending(x => x.Item2)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Folders whose path contains every word in order, with the last word in the folder's own name,
    /// so "proj api" finds "D:\Projects\shop\api".
    /// </summary>
    public IReadOnlyList<(string Path, double Score)> Match(string query, int count, DateTime? nowUtc = null)
    {
        string[] tokens = TextMatch.Tokenize(query);
        if (tokens.Length == 0) return Top(count, nowUtc);
        long now = ToUnix(nowUtc);
        var results = new List<(string, double)>();
        lock (_gate)
        {
            foreach (var (path, entry) in _map)
            {
                double nameScore = TextMatch.Score(PathUtil.DisplayName(path), tokens[^1]);
                if (nameScore <= 0 || !TokensInOrder(path, tokens)) continue;
                results.Add((path, nameScore + Boost(Frecency(entry, now))));
            }
        }
        return results.OrderByDescending(r => r.Item2).Take(count).ToList();
    }

    /// <summary>Extra search score for a path, capped so a frequent folder helps but never buries an exact name match.</summary>
    public double SearchBoost(string path) => Boost(Score(path));

    public static double Boost(double frecency) => frecency <= 0 ? 0 : Math.Min(40, 10 * Math.Log2(1 + frecency));

    /// <summary>Forgets folders that no longer exist.</summary>
    public int Prune()
    {
        List<string> paths;
        lock (_gate) paths = _map.Keys.ToList();
        var missing = paths.Where(p => !Directory.Exists(p) && !p.StartsWith(@"\\", StringComparison.Ordinal)).ToList();
        if (missing.Count == 0) return 0;
        lock (_gate)
        {
            foreach (string p in missing) _map.Remove(p);
            _dirty = true;
        }
        return missing.Count;
    }

    public void SaveIfDirty()
    {
        Dictionary<string, FrecencyEntry> snapshot;
        lock (_gate)
        {
            if (!_dirty) return;
            snapshot = new Dictionary<string, FrecencyEntry>(_map);
            _dirty = false;
        }
        try { JsonFile.Write(_file, snapshot, CoreJsonContext.Default.DictionaryStringFrecencyEntry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static double Frecency(FrecencyEntry e, long now)
    {
        long age = now - e.Last;
        double factor = age < 3600 ? 4 : age < 86400 ? 2 : age < 7 * 86400 ? 0.5 : 0.25;
        return e.Rank * factor;
    }

    /// <summary>Once the total grows past a cap, everything decays by 10% and rarely used folders drop off.</summary>
    private void Age()
    {
        double total = 0;
        foreach (var e in _map.Values) total += e.Rank;
        if (total <= MaxTotalRank) return;
        foreach (var key in _map.Keys.ToList())
        {
            var e = _map[key];
            e.Rank *= 0.9;
            if (e.Rank < 1) _map.Remove(key);
        }
    }

    private static bool TokensInOrder(string path, string[] tokens)
    {
        int pos = 0;
        foreach (string t in tokens)
        {
            int idx = path.IndexOf(t, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            pos = idx + t.Length;
        }
        return true;
    }

    private static long ToUnix(DateTime? utc) => new DateTimeOffset(utc ?? DateTime.UtcNow).ToUnixTimeSeconds();
}
