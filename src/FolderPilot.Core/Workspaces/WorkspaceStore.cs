using FolderPilot.Core.Storage;

namespace FolderPilot.Core.Workspaces;

/// <summary>A named set of folders that open together, e.g. everything for one project.</summary>
public sealed class Workspace
{
    public string Name { get; set; } = "";
    public List<string> Folders { get; set; } = [];
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastOpenedUtc { get; set; }
}

public sealed class WorkspaceStore
{
    private readonly object _gate = new();
    private readonly string _file;
    private readonly List<Workspace> _items;

    public WorkspaceStore(string? file = null)
    {
        _file = file ?? AppPaths.DataFile("workspaces.json");
        _items = JsonFile.Read(_file, CoreJsonContext.Default.ListWorkspace) ?? [];
    }

    public event Action? Changed;

    public IReadOnlyList<Workspace> All
    {
        get { lock (_gate) return _items.OrderByDescending(w => w.LastOpenedUtc ?? w.CreatedUtc).ToList(); }
    }

    /// <summary>Saves (or replaces) a workspace. Duplicate folders are dropped, order is kept.</summary>
    public Workspace Save(string name, IEnumerable<string> folders)
    {
        var workspace = new Workspace
        {
            Name = name.Trim(),
            Folders = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
        lock (_gate)
        {
            _items.RemoveAll(w => w.Name.Equals(workspace.Name, StringComparison.CurrentCultureIgnoreCase));
            _items.Add(workspace);
            Persist();
        }
        Changed?.Invoke();
        return workspace;
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            _items.RemoveAll(w => w.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            Persist();
        }
        Changed?.Invoke();
    }

    public void MarkOpened(string name)
    {
        lock (_gate)
        {
            var w = _items.FirstOrDefault(x => x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            if (w is null) return;
            w.LastOpenedUtc = DateTime.UtcNow;
            Persist();
        }
    }

    private void Persist()
    {
        try { JsonFile.Write(_file, _items, CoreJsonContext.Default.ListWorkspace); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
