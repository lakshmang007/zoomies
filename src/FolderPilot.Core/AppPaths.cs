namespace FolderPilot.Core;

/// <summary>Where FolderPilot keeps its own small data files (settings, history, learned folders).</summary>
public static class AppPaths
{
    private static string? _dataDir;

    public static string DataDir
    {
        get
        {
            if (_dataDir is null)
            {
                _dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderPilot");
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
}
