using System.IO;
using FolderPilot.Core;

namespace FolderPilot.Services;

/// <summary>Local error log (never sent anywhere), capped at 1 MB.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string FilePath => Path.Combine(AppPaths.DataDir, "logs", "errors.log");

    public static void Error(Exception ex, string context)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024) File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"[{DateTime.Now:O}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
