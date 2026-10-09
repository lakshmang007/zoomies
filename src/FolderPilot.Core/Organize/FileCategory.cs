namespace FolderPilot.Core.Organize;

public enum FileCategory { Documents, Images, Videos, Music, Archives, Installers, Code, Models, Fonts, DiskImages, Other }

public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> ByExtension = Build();

    public static FileCategory Of(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var category) ? category : FileCategory.Other;

    public static bool IsInstaller(string path) => Of(path) == FileCategory.Installers;

    /// <summary>Partial downloads and lock files that must never be moved.</summary>
    public static bool IsTemporary(string fileName)
    {
        if (fileName.StartsWith("~$", StringComparison.Ordinal) || fileName.StartsWith(".~lock", StringComparison.Ordinal))
            return true;
        string ext = Path.GetExtension(fileName);
        return ext.Equals(".crdownload", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".part", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".partial", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".download", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".opdownload", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".!ut", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".!qb", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsShortcut(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, string extensions)
        {
            foreach (string e in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map["." + e] = c;
        }

        Add(FileCategory.Documents,
            "pdf doc docx docm dot dotx odt rtf txt md tex wpd pages xls xlsx xlsm xlsb ods csv tsv numbers " +
            "ppt pptx pptm pps ppsx odp key epub mobi azw azw3 djvu xps oxps one vsdx pub msg eml ics vcf");
        Add(FileCategory.Images,
            "jpg jpeg jfif png gif bmp webp heic heif avif tif tiff svg ico psd ai eps xcf raw cr2 cr3 nef arw dng orf rw2 sketch fig");
        Add(FileCategory.Videos, "mp4 m4v mkv mov avi wmv webm flv mpg mpeg 3gp ts mts m2ts vob ogv");
        Add(FileCategory.Music, "mp3 m4a aac wav flac ogg oga opus wma aiff aif alac mid midi amr");
        Add(FileCategory.Archives, "zip rar 7z tar gz tgz bz2 xz zst cab lz lzma");
        Add(FileCategory.Installers, "exe msi msix msixbundle appx appxbundle apk xapk dmg pkg deb rpm");
        Add(FileCategory.Code,
            "c h cpp hpp cc cs java kt py ipynb js mjs ts tsx jsx html htm css scss json xml yaml yml toml ini " +
            "sql sh bat cmd ps1 psm1 go rs rb php swift dart lua r m vb fs sln slnx csproj gradle");
        // 3D printing, 3D modelling and CAD
        Add(FileCategory.Models, "stl obj 3mf fbx blend gltf glb dae ply step stp iges igs gcode bgcode skp dwg dxf f3d f3z max ma mb c4d usdz");
        Add(FileCategory.Fonts, "ttf otf woff woff2 fon");
        Add(FileCategory.DiskImages, "iso img vhd vhdx vmdk");
        return map;
    }
}
