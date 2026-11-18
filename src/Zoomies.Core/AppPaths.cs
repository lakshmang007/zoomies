namespace Zoomies.Core;

/// <summary>Where Zoomies keeps its own small data files (settings, history, learned folders).</summary>
public static class AppPaths
{
    private static string? _dataDir;

    /// <summary>
    /// %APPDATA%\Zoomies. Kept apart from %LOCALAPPDATA%\Zoomies, which the installer owns and replaces on every update.
    /// </summary>
    public static string DataDir
    {
        get
        {
            if (_dataDir is null)
            {
                _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zoomies");
                if (!Directory.Exists(_dataDir)) CopyFromOldName(_dataDir);
                Directory.CreateDirectory(_dataDir);
            }
            return _dataDir;
        }
    }

    /// <summary>Points all data files at another folder (used by tests and portable mode).</summary>
    public static void OverrideDataDir(string dir)
    {
        Directory.CreateDirectory(dir);
        _dataDir = dir;
    }

    public static string DataFile(string name) => Path.Combine(DataDir, name);

    /// <summary>The app used to be called FolderPilot; bring its settings, history and learned folders along once.</summary>
    private static void CopyFromOldName(string target)
    {
        string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderPilot");
        if (!Directory.Exists(old)) return;
        try
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(old, "*.json"))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
