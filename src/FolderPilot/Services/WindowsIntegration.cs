using FolderPilot.Localization;
using Microsoft.Win32;

namespace FolderPilot.Services;

/// <summary>Per-user registrations (no admin rights needed). Each one is fully removed when switched off.</summary>
internal static class WindowsIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FolderPilot";
    private static readonly string[] MenuParents = [@"Software\Classes\Directory\shell", @"Software\Classes\Directory\Background\shell"];
    private const string MenuKey = "FolderPilot";

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

    /// <summary>A "FolderPilot ▸" cascading menu on folders and on the empty space inside a folder.</summary>
    private static void AddMenu(string parent)
    {
        using var root = Registry.CurrentUser.CreateSubKey($@"{parent}\{MenuKey}");
        root.SetValue("MUIVerb", "FolderPilot");
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
            if (startup) StartsWithWindows = true;
            if (contextMenu) HasContextMenu = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
