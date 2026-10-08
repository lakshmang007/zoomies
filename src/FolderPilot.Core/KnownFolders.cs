using System.Runtime.InteropServices;

namespace FolderPilot.Core;

/// <summary>The user's standard folders, resolved through the shell so redirected folders (OneDrive backup, D:\ moves) are found.</summary>
public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public static string Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public static string Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    public static string Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public static string Downloads => GetKnownFolder(DownloadsId) ?? Path.Combine(UserProfile, "Downloads");

    /// <summary>OneDrive's local folder when it lives outside the user profile, otherwise null.</summary>
    public static string? OneDrive
    {
        get
        {
            string? dir = Environment.GetEnvironmentVariable("OneDrive");
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }
    }

    /// <summary>Folders that hold personal files and are worth cleaning up.</summary>
    public static IReadOnlyList<string> PersonalFolders() =>
        PathUtil.RemoveNested(new[] { Downloads, Desktop, Documents, Pictures, Videos, Music }
            .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)));

    /// <summary>Default search roots: the whole user profile (AppData and dot-folders are filtered out) plus OneDrive.</summary>
    public static IReadOnlyList<string> DefaultIndexRoots()
    {
        var roots = new List<string> { UserProfile };
        if (OneDrive is { } od) roots.Add(od);
        // Known folders redirected to another drive (e.g. Documents on D:\) are not under the profile.
        roots.AddRange(PersonalFolders());
        return PathUtil.RemoveNested(roots.Where(Directory.Exists));
    }

    private static string? GetKnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out IntPtr ptr) != 0) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        catch (Exception) { return null; }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
