using FolderPilot.Core.Storage;

namespace FolderPilot.Core.Settings;

public static class SettingsStore
{
    public static string FilePath => AppPaths.DataFile("settings.json");

    public static AppSettings Load() =>
        JsonFile.Read(FilePath, CoreJsonContext.Default.AppSettings) ?? new AppSettings();

    public static void Save(AppSettings settings) =>
        JsonFile.Write(FilePath, settings, CoreJsonContext.Default.AppSettings);
}
