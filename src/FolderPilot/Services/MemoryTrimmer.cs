using System.Diagnostics;
using System.Runtime;
using System.Windows.Threading;
using FolderPilot.Interop;

namespace FolderPilot.Services;

/// <summary>
/// When FolderPilot has been idle in the tray for a while, it compacts its memory and hands the
/// unused pages back to Windows, so it does not hold RAM the user's other apps could use.
/// </summary>
internal static class MemoryTrimmer
{
    private static DispatcherTimer? _timer;

    public static bool Enabled { get; set; } = true;

    public static void TrimSoon(TimeSpan delay)
    {
        if (!Enabled) return;
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    public static void Cancel() => _timer?.Stop();

    public static void TrimNow()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), -1, -1);
    }

    /// <summary>Private memory in bytes — the number Task Manager's "Memory" column shows.</summary>
    public static long CurrentUsage()
    {
        using var p = Process.GetCurrentProcess();
        return p.PrivateMemorySize64;
    }

    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Enabled) TrimNow();
        };
        return timer;
    }
}
