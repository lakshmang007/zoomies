using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Zoomies.Core.Frecency;

namespace Zoomies.Services;

/// <summary>
/// Gives Quick Jump useful results on the very first run by reading what Windows already knows:
/// the "Recent" items list and Quick Access pinned/frequent folders.
/// </summary>
internal static class FolderSeeder
{
    private const string QuickAccess = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

    /// <summary>Runs on its own STA thread (shell COM objects need one) at low priority.</summary>
    public static void SeedInBackground(FrecencyStore store, Action done)
    {
        var thread = new Thread(() =>
        {
            try
            {
                SeedFromQuickAccess(store);
                SeedFromRecent(store);
                store.SaveIfDirty();
            }
            catch (Exception ex) { Log.Error(ex, "Seeding folders"); }
            done();
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Zoomies seeding" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void SeedFromQuickAccess(FrecencyStore store)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            dynamic? folder = shell.NameSpace(QuickAccess);
            if (folder is null) return;
            foreach (dynamic item in folder.Items())
            {
                string? path = item.Path;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) store.Visit(path, weight: 3);
            }
        }
        catch (COMException) { }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
    }

    private static void SeedFromRecent(FrecencyStore store)
    {
        string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!Directory.Exists(recent)) return;
        var links = new DirectoryInfo(recent).EnumerateFiles("*.lnk")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(300)
            .ToList();

        foreach (var link in links)
        {
            string? target = ResolveShortcut(link.FullName);
            if (string.IsNullOrEmpty(target)) continue;
            if (Directory.Exists(target))
                store.Visit(target, weight: 1, whenUtc: link.LastWriteTimeUtc);
            else if (File.Exists(target) && Path.GetDirectoryName(target) is { } parent)
                store.Visit(parent, weight: 0.5, whenUtc: link.LastWriteTimeUtc);
        }
    }

    private static string? ResolveShortcut(string lnk)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
    }
}
