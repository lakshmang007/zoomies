using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Zoomies.Core.Storage;

/// <summary>Reads and writes small JSON data files. Writes go to a temp file first so a crash never leaves a half-written file.</summary>
public static class JsonFile
{
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            if (!File.Exists(path)) return default;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt file is moved aside instead of crashing the app or being silently overwritten.
            TryBackupCorrupt(path);
            return default;
        }
    }

    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, value, typeInfo);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            if (File.Exists(path)) File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
