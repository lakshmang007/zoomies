using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Zoomies.Core.Platform;

/// <summary>A Recycle Bin entry, read from its "$I" metadata file.</summary>
public sealed record RecycledItem(string OriginalPath, long Size, DateTime DeletedUtc, string InfoFile, string DataPath);

/// <summary>
/// Sends files to the Windows Recycle Bin (never a permanent delete) and restores them for Undo.
/// Restore works on the Recycle Bin's own file format, so it does not depend on the Windows display language.
/// </summary>
public static class RecycleBin
{
    /// <summary>Only fixed local drives have a Recycle Bin. On USB sticks and network shares a "delete" would be permanent, so Zoomies refuses.</summary>
    public static bool CanRecycle(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return false; }
    }

    /// <summary>Moves the items to the Recycle Bin and returns the ones that are gone afterwards.</summary>
    public static IReadOnlyList<string> Send(IReadOnlyList<string> paths, IntPtr ownerWindow = default)
    {
        var eligible = paths.Where(p => CanRecycle(p) && p.Length < 260 && (File.Exists(p) || Directory.Exists(p))).ToList();
        var recycled = new List<string>();
        // Batches keep each shell call short and let Explorer group them into one undo step.
        foreach (var batch in eligible.Chunk(200))
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = ownerWindow,
                wFunc = FO_DELETE,
                pFrom = string.Join('\0', batch) + "\0\0",
                // FOF_WANTNUKEWARNING makes Windows ask first if an item is too big for the Recycle Bin and would be destroyed.
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
            };
            SHFileOperation(ref op);
            recycled.AddRange(batch.Where(p => !File.Exists(p) && !Directory.Exists(p)));
        }
        return recycled;
    }

    /// <summary>Puts a recycled item back where it was. If something new now has that name, the restored copy gets a "(2)" suffix.</summary>
    public static bool TryRestore(string originalPath, DateTime deletedAfterUtc, out string restoredTo)
    {
        restoredTo = "";
        RecycledItem? item = Find(originalPath, deletedAfterUtc);
        if (item is null) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            restoredTo = PathUtil.UniquePath(originalPath);
            if (Directory.Exists(item.DataPath)) Directory.Move(item.DataPath, restoredTo);
            else File.Move(item.DataPath, restoredTo);
            File.Delete(item.InfoFile);
            Shell.NotifyFolderChanged(Path.GetDirectoryName(restoredTo)!);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The most recent recycled copy of <paramref name="originalPath"/> deleted after the given time.</summary>
    public static RecycledItem? Find(string originalPath, DateTime deletedAfterUtc) =>
        Enumerate(originalPath)
            .Where(i => i.OriginalPath.Equals(originalPath, StringComparison.OrdinalIgnoreCase) && i.DeletedUtc >= deletedAfterUtc)
            .MaxBy(i => i.DeletedUtc);

    /// <summary>Items in the current user's Recycle Bin on the drive that holds <paramref name="pathOnDrive"/>.</summary>
    public static IEnumerable<RecycledItem> Enumerate(string pathOnDrive)
    {
        string? root = Path.GetPathRoot(pathOnDrive);
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(root) || sid is null) yield break;

        string bin = Path.Combine(root, "$Recycle.Bin", sid);
        IEnumerable<string> infoFiles;
        try { infoFiles = Directory.EnumerateFiles(bin, "$I*").ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }

        foreach (string info in infoFiles)
        {
            RecycledItem? item = null;
            try
            {
                var parsed = ParseInfo(File.ReadAllBytes(info));
                if (parsed is { } p)
                {
                    string data = Path.Combine(bin, "$R" + Path.GetFileName(info)[2..]);
                    item = new RecycledItem(p.Path, p.Size, p.DeletedUtc, info, data);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (item is not null) yield return item;
        }
    }

    /// <summary>
    /// Parses a "$I" file. Version 1 (Windows Vista–8.1): 24-byte header + fixed 520-byte path.
    /// Version 2 (Windows 10+): 24-byte header + 4-byte character count + path.
    /// </summary>
    internal static (string Path, long Size, DateTime DeletedUtc)? ParseInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 28) return null;
        long version = BinaryPrimitives.ReadInt64LittleEndian(data);
        long size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);

        ReadOnlySpan<byte> pathBytes;
        if (version == 1)
        {
            pathBytes = data[24..Math.Min(data.Length, 24 + 520)];
        }
        else if (version == 2)
        {
            int chars = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
            if (chars <= 0 || 28 + chars * 2L > data.Length) return null;
            pathBytes = data.Slice(28, chars * 2);
        }
        else return null;

        string path = Encoding.Unicode.GetString(pathBytes);
        int nul = path.IndexOf('\0');
        if (nul >= 0) path = path[..nul];
        if (path.Length == 0) return null;

        DateTime deleted;
        try { deleted = DateTime.FromFileTimeUtc(fileTime); }
        catch (ArgumentOutOfRangeException) { return null; }
        return (path, size, deleted);
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    // Layout matches 64-bit Windows (x64 and ARM64), the only platforms Zoomies ships for.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
