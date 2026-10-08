using System.IO;
using FolderPilot.Core;
using FolderPilot.Core.Organize;
using FolderPilot.Localization;

namespace FolderPilot.UI;

/// <summary>Formatting shared by the windows: icons, sizes, dates and short paths.</summary>
internal static class Display
{
    public const string FolderGlyph = "";

    public static string Glyph(string path, bool isDirectory) => isDirectory ? FolderGlyph : FileCategories.Of(path) switch
    {
        FileCategory.Documents => "",
        FileCategory.Images => "",
        FileCategory.Videos => "",
        FileCategory.Music => "",
        FileCategory.Archives => "",
        FileCategory.Installers => "",
        FileCategory.Code => "",
        FileCategory.Fonts => "",
        FileCategory.DiskImages => "",
        _ => "",
    };

    public static string Size(long bytes) => SizeFormat.Bytes(bytes, Loc.Culture);

    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("d", Loc.Culture);

    public static string DateAndTime(DateTime utc) => utc.ToLocalTime().ToString("g", Loc.Culture);

    /// <summary>"~\Documents\College" for paths in the user folder; long paths keep their start and end.</summary>
    public static string ShortPath(string path, int max = 70)
    {
        string profile = KnownFolders.UserProfile;
        if (PathUtil.IsSameOrInside(path, profile)) path = "~" + path[profile.Length..];
        if (path.Length <= max) return path;
        string root = path.StartsWith('~') ? "~" : Path.GetPathRoot(path) ?? "";
        int keep = max - root.Length - 2;
        return root + "\\…" + path[^keep..];
    }
}
