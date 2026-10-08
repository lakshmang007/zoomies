namespace FolderPilot.Core.Tests;

/// <summary>A throwaway folder tree under %TEMP% for one test.</summary>
public sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FolderPilotTests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(string relative) => System.IO.Path.Combine(Root, relative);

    public string File(string relative, string content = "x", DateTime? modifiedUtc = null)
    {
        string full = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        if (modifiedUtc is { } t)
        {
            System.IO.File.SetCreationTimeUtc(full, t);
            System.IO.File.SetLastWriteTimeUtc(full, t);
            System.IO.File.SetLastAccessTimeUtc(full, t);
        }
        return full;
    }

    public string Dir(string relative)
    {
        string full = Path(relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
