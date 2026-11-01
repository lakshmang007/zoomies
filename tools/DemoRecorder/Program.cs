using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace DemoRecorder;

internal sealed record Options(string Root, string App, string Output, string Ffmpeg, string Encoder, int ScreenWidth, int ScreenHeight, int CountdownSeconds);

/// <summary>
/// Usage:
///   DemoRecorder setup  [--root C:\Demo]                      create the sample PC only
///   DemoRecorder record [--root C:\Demo] [--app FolderPilot.exe] [--out demo.mp4] [--encoder h264_nvenc]
/// Recording takes over the mouse and keyboard for about 90 seconds.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "record";
        string Arg(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        string repo = FindRepoRoot();
        string root = Arg("--root", @"C:\Demo");
        if (command == "setup")
        {
            DemoData.Create(root);
            Console.WriteLine($"Demo PC created in {root}");
            return 0;
        }
        if (command == "probe")
        {
            // Diagnostic: opens the Organize preview in the demo copy and lists what UI Automation can see. No input is sent.
            string appExe = Arg("--app", Path.Combine(repo, @"src\FolderPilot\bin\Debug\net10.0-windows\FolderPilot.exe"));
            DemoData.Create(root);
            var start = new System.Diagnostics.ProcessStartInfo(appExe, $"--organize \"{Path.Combine(root, "Downloads")}\"") { UseShellExecute = false };
            start.Environment["FOLDERPILOT_DEMO_ROOT"] = root;
            System.Diagnostics.Process.Start(start)?.Dispose();
            IntPtr hub = Ui.WaitWindow(h => Ui.TitleOf(h) == "FolderPilot" && Ui.ClassOf(h).StartsWith("HwndWrapper", StringComparison.Ordinal), "hub", 15000);
            Thread.Sleep(3000);
            if (args.Contains("--repreview"))
            {
                // A second preview rebuilds the type list, the case that used to hide the folder boxes from UI Automation.
                var preview = Ui.WaitElement(hub, "Preview", System.Windows.Automation.ControlType.Button, 5000);
                ((System.Windows.Automation.InvokePattern)preview.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
                Thread.Sleep(2000);
            }
            if (Array.IndexOf(args, "--search") is int si and >= 0 && si + 1 < args.Length)
            {
                var box = Ui.WaitElement(hub, n => n.StartsWith("Find a file type", StringComparison.Ordinal), "search", 5000, System.Windows.Automation.ControlType.Edit);
                ((System.Windows.Automation.ValuePattern)box.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).SetValue(args[si + 1]);
                Thread.Sleep(1500);
                try
                {
                    var found = Ui.WaitElement(hub, "Folder for .stl files", System.Windows.Automation.ControlType.Edit, 4000);
                    Console.WriteLine($"FOUND after search: {found.Cached.BoundingRectangle} offscreen={found.Cached.IsOffscreen}");
                }
                catch (TimeoutException) { Console.WriteLine("NOT FOUND after search (with on-screen filter)"); }
            }
            var cache = new System.Windows.Automation.CacheRequest();
            cache.Add(System.Windows.Automation.AutomationElement.NameProperty);
            cache.Add(System.Windows.Automation.AutomationElement.ControlTypeProperty);
            using (cache.Activate())
            {
                var all = System.Windows.Automation.AutomationElement.FromHandle(hub)
                    .FindAll(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                foreach (System.Windows.Automation.AutomationElement e in all)
                {
                    var c = e.Cached;
                    if (c.ControlType == System.Windows.Automation.ControlType.Edit || c.ControlType == System.Windows.Automation.ControlType.Button
                        || c.ControlType == System.Windows.Automation.ControlType.DataItem || c.ControlType == System.Windows.Automation.ControlType.ListItem)
                        Console.WriteLine($"{c.ControlType.ProgrammaticName,-24} {c.Name}");
                }
                Console.WriteLine($"total elements: {all.Count}");
            }
            var quit = new System.Diagnostics.ProcessStartInfo(appExe, "--quit") { UseShellExecute = false };
            quit.Environment["FOLDERPILOT_DEMO_ROOT"] = root;
            System.Diagnostics.Process.Start(quit)?.WaitForExit(10000);
            return 0;
        }
        if (command != "record")
        {
            Console.Error.WriteLine("Commands: setup, record, probe");
            return 2;
        }

        string ffmpeg = Arg("--ffmpeg", ScreenRecorder.FindFfmpeg() ?? "");
        if (!File.Exists(ffmpeg))
        {
            Console.Error.WriteLine("ffmpeg not found. Pass --ffmpeg <path> (one with gdigrab, e.g. from CapCut).");
            return 2;
        }
        var options = new Options(
            Root: root,
            App: Arg("--app", Path.Combine(repo, @"src\FolderPilot\bin\Debug\net10.0-windows\FolderPilot.exe")),
            Output: Path.GetFullPath(Arg("--out", Path.Combine(repo, "demo", "FolderPilot-demo.mp4"))),
            Ffmpeg: ffmpeg,
            Encoder: Arg("--encoder", "h264_nvenc"),
            ScreenWidth: GetSystemMetrics(0),
            ScreenHeight: GetSystemMetrics(1),
            CountdownSeconds: int.Parse(Arg("--countdown", "10")));

        DemoData.Create(root); // fresh files every take, since the demo moves some of them
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var overlay = new Overlay(options.ScreenWidth, options.ScreenHeight, Path.Combine(repo, @"src\FolderPilot\Assets\logo.png"));
        int exitCode = 0;
        // The script runs off the UI thread: UI Automation must not query windows owned by the thread it runs on.
        Task.Run(() =>
        {
            try
            {
                new DemoScript(options, overlay, app.Dispatcher).Run();
                Console.WriteLine($"Saved {options.Output}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Demo stopped: {ex.Message}");
                exitCode = 1;
            }
            finally
            {
                app.Dispatcher.Invoke(app.Shutdown);
            }
        });
        app.Run();
        return exitCode;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FolderPilot.slnx"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
