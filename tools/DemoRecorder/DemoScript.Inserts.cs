using System.IO;
using System.Windows.Automation;
using static DemoRecorder.Input;

namespace DemoRecorder;

/// <summary>
/// Clean shots of the real app for the live-action ad (docs/AD_FLOW.md): one short clip per moment, with no captions
/// or key hints, so they can be cut between the AI-generated scenes. The letters match the INSERT rows in that doc.
/// Shots are recorded in the order the sample PC allows, not the order of the ad.
/// </summary>
internal sealed partial class DemoScript
{
    private void RunInserts()
    {
        _quiet = true;
        Directory.CreateDirectory(options.Output);

        Focus(_explorer);
        Shot("A-find-intern", () =>
        {
            Hotkey();
            Type("intern", 120);
            Thread.Sleep(700);
            Chord(Ctrl, Enter); // show it in the folder: highlighted among 300 files
        }, 2200);

        Focus(_explorer);
        Shot("B-jump-sem5", () => Jump("sem 5"), 2000);

        Focus(_explorer);
        Jump("downl");
        Thread.Sleep(1200);
        Shot("C-organize-all", () =>
        {
            Hotkey(); // Quick Jump offers "Organize this folder" for the folder Explorer is showing
            Thread.Sleep(900);
            Press(Enter);
            _hub = Ui.WaitWindow(h => Ui.TitleOf(h) == "Zoomies" && Ui.ClassOf(h).StartsWith("HwndWrapper", StringComparison.Ordinal), "Zoomies window");
            Ui.Place(_hub, 950, 12, 955, 1015);
            var organize = Ui.WaitElement(_hub, "Organize now", ControlType.Button, timeoutMs: 15000);
            Thread.Sleep(1300);
            Ui.Click(_hub, organize);
        }, 2600);

        // Put the files back and plan again, so the next shot has a messy folder to sort.
        Ui.Click(_hub, Ui.WaitElement(_hub, "Undo", ControlType.Button), 300);
        Thread.Sleep(1800);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Preview", ControlType.Button), 300);
        Ui.WaitElement(_hub, "Organize now", ControlType.Button, timeoutMs: 15000);
        Thread.Sleep(1000);

        Shot("G-stl-folder", () =>
        {
            Ui.Click(_hub, Ui.WaitElement(_hub, n => n.StartsWith("Find a file type", StringComparison.Ordinal), "search box", type: ControlType.Edit));
            EnsureForeground(_hub, "Zoomies window");
            Type("stl", 150);
            Thread.Sleep(1000);
            Ui.Click(_hub, Ui.WaitElement(_hub, "Folder for .stl files", ControlType.Edit));
            Chord(Ctrl, A);
            Type("3D Prints", 105);
            Thread.Sleep(1300);
            Ui.Click(_hub, Ui.WaitElement(_hub, "Organize now", ControlType.Button));
        }, 2600);

        Ui.Place(_hub, 280, 12, 1360, 1015);
        Thread.Sleep(800);
        Shot("H-undo", () =>
        {
            Ui.Click(_hub, Ui.WaitElement(_hub, "History", ControlType.ListItem), 450);
            Thread.Sleep(900);
            Ui.Click(_hub, Ui.WaitElement(_hub, "Undo", ControlType.Button), 450);
        }, 2000);

        Shot("D-recycle-unused", () =>
        {
            Ui.Click(_hub, Ui.WaitElement(_hub, "Clean up", ControlType.ListItem), 450);
            Ui.Click(_hub, Ui.WaitElement(_hub, "Scan", ControlType.Button), 450);
            var selectAll = Ui.WaitElement(_hub, "Select all", ControlType.Button, timeoutMs: 30000);
            Thread.Sleep(1200);
            Ui.Click(_hub, selectAll, 450);
            Ui.Click(_hub, Ui.WaitElement(_hub, "Move to Recycle Bin", ControlType.Button), 450);
            IntPtr confirm = Ui.WaitWindow(h => Ui.ClassOf(h) == "#32770" && Ui.TitleOf(h) == "Move to Recycle Bin?", "confirmation");
            Thread.Sleep(800);
            Ui.Click(confirm, Ui.WaitElement(confirm, "Yes", ControlType.Button), 400);
        }, 2200);

        Shot("E-duplicates", () =>
        {
            Ui.Click(_hub, Ui.WaitElement(_hub, "Duplicates", ControlType.RadioButton), 450);
            Ui.Click(_hub, Ui.WaitElement(_hub, "Scan", ControlType.Button), 450);
            Ui.WaitElement(_hub, "Select all but the oldest copy", ControlType.Button, timeoutMs: 30000);
        }, 2600);

        Ui.Place(_hub, 960, 12, 945, 1015); // make room for the notes app
        Thread.Sleep(600);
        Shot("F-save-dialog", () => SaveDialogJump("invoices", 1900), 300);

        Shot("I-no-matches", () =>
        {
            Hotkey();
            Type("motivation", 95);
        }, 2600);
        Press(Escape);
        Thread.Sleep(400);
    }

    /// <summary>Records one clip: a short lead-in, the action, then <paramref name="holdMs"/> on the result.</summary>
    private void Shot(string name, Action action, int holdMs)
    {
        var recorder = new ScreenRecorder(options.Ffmpeg, Path.Combine(options.Output, name + ".mp4"),
            options.ScreenWidth, options.ScreenHeight, options.Encoder);
        try
        {
            Thread.Sleep(400);
            action();
            Thread.Sleep(holdMs);
        }
        finally
        {
            recorder.Stop();
        }
        Console.WriteLine($"  {name}.mp4");
    }
}
