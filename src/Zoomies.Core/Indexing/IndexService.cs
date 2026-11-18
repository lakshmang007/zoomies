using System.Collections.Concurrent;

namespace Zoomies.Core.Indexing;

public enum IndexState { NotStarted, Building, Ready }

/// <summary>
/// Owns the search index: builds it on a low-priority background thread, then keeps it current with
/// file-system change notifications instead of rescanning.
/// </summary>
public sealed class IndexService : IDisposable
{
    private const int MaxQueuedChanges = 5000;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<(WatcherChangeTypes Kind, string Path, string? OldPath)> _changes = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _applyTimer;
    private readonly Timer _rebuildTimer;
    private FileIndex? _index;
    private CancellationTokenSource? _buildCts;
    private IReadOnlyList<string> _roots = [];
    private PathFilter _filter = PathFilter.Default;
    private int _maxEntries = 1_000_000;
    private volatile int _progress;
    private volatile bool _overflowed;

    public IndexService()
    {
        _applyTimer = new Timer(_ => ApplyChanges());
        _rebuildTimer = new Timer(_ => Rebuild());
    }

    public event Action? StateChanged;

    public IndexState State { get; private set; }
    public int ItemsSoFar => State == IndexState.Building ? _progress : ItemCount;
    public int ItemCount => _index?.Count ?? 0;
    public long MemoryBytes => _index?.ApproximateBytes ?? 0;
    public DateTime? LastBuiltUtc { get; private set; }
    public TimeSpan LastBuildDuration { get; private set; }
    public Exception? LastError { get; private set; }
    public IReadOnlyList<string> Roots => _roots;

    public void Configure(IReadOnlyList<string> roots, PathFilter filter, int maxEntries, TimeSpan delay)
    {
        lock (_gate)
        {
            _roots = roots;
            _filter = filter;
            _maxEntries = maxEntries;
        }
        _rebuildTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public void Rebuild()
    {
        CancellationTokenSource cts;
        IReadOnlyList<string> roots;
        PathFilter filter;
        int max;
        lock (_gate)
        {
            _buildCts?.Cancel();
            _buildCts = cts = new CancellationTokenSource();
            roots = _roots;
            filter = _filter;
            max = _maxEntries;
            State = IndexState.Building;
            _progress = 0;
        }
        StateChanged?.Invoke();

        // A dedicated below-normal thread keeps indexing from competing with whatever the user is doing.
        var thread = new Thread(() =>
        {
            var started = DateTime.UtcNow;
            try
            {
                var index = FileIndex.Build(roots, filter, max, p => _progress = p, cts.Token);
                lock (_gate)
                {
                    if (cts.IsCancellationRequested) return;
                    _index = index;
                    _overflowed = false;
                    _changes.Clear();
                    State = IndexState.Ready;
                    LastBuiltUtc = DateTime.UtcNow;
                    LastBuildDuration = DateTime.UtcNow - started;
                    StartWatchers(roots, filter);
                }
                StateChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Never let one odd folder take the app down: keep the previous index and report the problem.
                LastError = ex;
                lock (_gate) State = _index is null ? IndexState.NotStarted : IndexState.Ready;
                StateChanged?.Invoke();
            }
        }, maxStackSize: 512 * 1024)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "Zoomies indexer",
        };
        thread.Start();
    }

    public List<SearchHit> Search(string query, int maxResults, SearchScope scope = SearchScope.All,
        Func<string, double>? boost = null, CancellationToken ct = default) =>
        _index?.Search(query, maxResults, scope, boost, ct) ?? [];

    // ───────────────────────── change tracking ─────────────────────────

    /// <summary>
    /// One non-recursive watcher on each root plus a recursive one on each allowed top-level folder.
    /// This keeps busy excluded trees (like AppData under the user profile) from waking Zoomies up.
    /// </summary>
    private void StartWatchers(IReadOnlyList<string> roots, PathFilter filter)
    {
        StopWatchers();
        foreach (string root in roots)
        {
            TryWatch(root, recursive: false);
            IEnumerable<string> children;
            try
            {
                children = new DirectoryInfo(root)
                    .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = filter.AttributesToSkip })
                    .Where(d => !filter.IsExcludedDirectory(root, d.Name))
                    .Select(d => d.FullName)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string child in children) TryWatch(child, recursive: true);
        }
    }

    private void TryWatch(string path, bool recursive)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 16 * 1024,
            };
            watcher.Created += (_, e) => Enqueue(WatcherChangeTypes.Created, e.FullPath, null);
            watcher.Deleted += (_, e) => Enqueue(WatcherChangeTypes.Deleted, e.FullPath, null);
            watcher.Renamed += (_, e) => Enqueue(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
            watcher.Error += (_, _) => { _overflowed = true; ScheduleApply(); };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
    }

    private void StopWatchers()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    private void Enqueue(WatcherChangeTypes kind, string path, string? oldPath)
    {
        string? root = _roots.FirstOrDefault(r => PathUtil.IsSameOrInside(path, r));
        if (root is null || _filter.IsExcludedPath(path, root)) return;
        if (_changes.Count >= MaxQueuedChanges) _overflowed = true;
        else _changes.Enqueue((kind, path, oldPath));
        ScheduleApply();
    }

    /// <summary>Changes are applied in one batch after a quiet second, so a big copy costs one update, not thousands.</summary>
    private void ScheduleApply() => _applyTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

    private void ApplyChanges()
    {
        if (_overflowed)
        {
            // Too much happened to replay; rebuild once things settle.
            _overflowed = false;
            _changes.Clear();
            _rebuildTimer.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
            return;
        }

        var index = _index;
        if (index is null) return;
        bool rootLevelChange = false;
        while (_changes.TryDequeue(out var change))
        {
            try
            {
                switch (change.Kind)
                {
                    case WatcherChangeTypes.Created: index.ApplyCreated(change.Path); break;
                    case WatcherChangeTypes.Deleted: index.ApplyDeleted(change.Path); break;
                    case WatcherChangeTypes.Renamed: index.ApplyRenamed(change.OldPath!, change.Path); break;
                }
                string? parent = Path.GetDirectoryName(change.Path);
                if (change.Kind != WatcherChangeTypes.Deleted && parent is not null
                    && _roots.Any(r => r.Equals(parent, StringComparison.OrdinalIgnoreCase))
                    && Directory.Exists(change.Path))
                    rootLevelChange = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        // A new top-level folder needs its own watcher.
        if (rootLevelChange)
            lock (_gate) StartWatchers(_roots, _filter);

        // Deleted entries are only flagged; reclaim their memory once they pile up.
        if (index.DeletedCount > 20_000 && index.DeletedCount > index.Count / 4)
            _rebuildTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _buildCts?.Cancel();
        _applyTimer.Dispose();
        _rebuildTimer.Dispose();
        lock (_gate) StopWatchers();
    }
}
