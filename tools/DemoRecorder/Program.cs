using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace DemoRecorder;

internal sealed record Options(string Root, string App, string Output, string Ffmpeg, string Encoder, int ScreenWidth, int ScreenHeight);

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
        if (command != "record")
        {
            Console.Error.WriteLine("Commands: setup, record");
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
            ScreenHeight: GetSystemMetrics(1));

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
