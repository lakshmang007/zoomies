using Zoomies.Localization;
using Microsoft.Win32;

namespace Zoomies.Services;

/// <summary>Per-user registrations (no admin rights needed). Each one is fully removed when switched off.</summary>
internal static class WindowsIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Zoomies";
    private static readonly string[] MenuParents = [@"Software\Classes\Directory\shell", @"Software\Classes\Directory\Background\shell"];
    private const string MenuKey = "Zoomies";

    private static string Exe => Environment.ProcessPath!;

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Contains(Exe, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Exe}\" --background");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool HasContextMenu
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{MenuParents[0]}\{MenuKey}");
            return key is not null;
        }
        set
        {
            foreach (string parent in MenuParents)
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{parent}\{MenuKey}", throwOnMissingSubKey: false);
                if (value) AddMenu(parent);
            }
        }
    }

    /// <summary>A "Zoomies ▸" cascading menu on folders and on the empty space inside a folder.</summary>
    private static void AddMenu(string parent)
    {
        using var root = Registry.CurrentUser.CreateSubKey($@"{parent}\{MenuKey}");
        root.SetValue("MUIVerb", "Zoomies");
        root.SetValue("Icon", $"\"{Exe}\",0");
        root.SetValue("SubCommands", "");
        (string Id, string TextKey, string Arg)[] items =
        [
            ("1organize", "Action.OrganizeHere", "--organize"),
            ("2unused", "Action.CleanupHere", "--cleanup"),
            ("3duplicates", "Action.DuplicatesHere", "--duplicates"),
        ];
        foreach (var (id, textKey, arg) in items)
        {
            using var verb = root.CreateSubKey($@"shell\{id}");
            verb.SetValue("MUIVerb", Loc.T(textKey));
            using var command = verb.CreateSubKey("command");
            command.SetValue("", $"\"{Exe}\" {arg} \"%V\"");
        }
    }

    /// <summary>Keeps registrations pointing at this copy of the app if it was moved or updated.</summary>
    public static void Refresh(bool startup, bool contextMenu)
    {
        try
        {
            RemoveOldName();
            if (startup) StartsWithWindows = true;
            if (contextMenu) HasContextMenu = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    /// <summary>Removes every registration; the uninstaller calls this so nothing is left pointing at a deleted app.</summary>
    public static void RemoveAll()
    {
        try
        {
            StartsWithWindows = false;
            HasContextMenu = false;
            RemoveOldName();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    /// <summary>Registrations left by the app's earlier name, FolderPilot.</summary>
    private static void RemoveOldName()
    {
        const string old = "FolderPilot";
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            key?.DeleteValue(old, throwOnMissingValue: false);
        foreach (string parent in MenuParents)
            Registry.CurrentUser.DeleteSubKeyTree($@"{parent}\{old}", throwOnMissingSubKey: false);
    }
}
