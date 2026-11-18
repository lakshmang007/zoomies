using System.Windows.Automation;
using static DemoRecorder.Input;

namespace DemoRecorder;

/// <summary>
/// "Found it." — a short story-style ad in four beats (hook, spread, montage, punchline), told with speech bubbles
/// over the real app. Riya finds a file in a 300-file Downloads folder, everyone around her starts using Zoomies,
/// and it ends on a joke the app answers itself.
/// </summary>
internal sealed partial class DemoScript
{
    private const string Riya = "#F5B14C", Sam = "#5EE08A", Mei = "#F472B6", Leo = "#7DD3FC";

    private void RunAd()
    {
        // ── Hook: the "impossible" find ──
        Thread.Sleep(700);
        Say("Sam", "Riya, where's the internship letter?", Sam, 2300);
        Say("Riya", "One sec.", Riya, 900);
        Focus(_explorer);
        Hotkey();
        Type("intern", 120);
        Thread.Sleep(800);
        ShowKeys("Ctrl + Enter");
        Chord(Ctrl, Enter); // show it in the folder: highlighted among hundreds of files
        Thread.Sleep(1700);
        Say("Sam", "Woah. How did you find that?", Sam, 2000);
        Say("Riya", "I just typed “intern”.", Riya, 2000);
        OnUi(overlay.HideSay);

        // ── Spread: everyone tests it ──
        Beat("Riya found a file in her Downloads.", 2100);
        Say("Mei", "Impossible. Her Downloads has 300 files.", Mei, 2500);
        Say("Leo", "Interesting… can it find last semester's notes?", Leo, 2200);
        Focus(_explorer);
        Jump("sem 5");
        Thread.Sleep(1600);
        Say("Mei", "So we can all find our files now?", Mei, 2100);
        Say("Riya", "Yeah. It even tidies them.", Riya, 1500);
        Jump("downl");
        Thread.Sleep(1000);
        Hotkey(); // Quick Jump offers "Organize this folder" for the folder Explorer is showing
        Thread.Sleep(1000);
        ShowKeys("Enter");
        Press(Enter);
        _hub = Ui.WaitWindow(h => Ui.TitleOf(h) == "Zoomies" && Ui.ClassOf(h).StartsWith("HwndWrapper", StringComparison.Ordinal), "Zoomies window");
        Ui.Place(_hub, 950, 12, 955, 1015);
        var organize = Ui.WaitElement(_hub, "Organize now", ControlType.Button, timeoutMs: 15000);
        Thread.Sleep(1100);
        Ui.Click(_hub, organize);
        Thread.Sleep(2300);
        Say("Riya", "Done.", Riya, 1400);
        OnUi(overlay.HideSay);

        // ── Montage: then everyone wanted it ──
        Beat("Then everyone wanted it.", 1600);
        Ui.Place(_hub, 280, 12, 1360, 1015);

        Say("Leo", "Just freed 239 MB of old lecture videos.", Leo, 0);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Clean up", ControlType.ListItem), 450);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Scan", ControlType.Button), 450);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Select all", ControlType.Button, timeoutMs: 30000), 450);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Move to Recycle Bin", ControlType.Button), 450);
        IntPtr confirm = Ui.WaitWindow(h => Ui.ClassOf(h) == "#32770" && Ui.TitleOf(h) == "Move to Recycle Bin?", "confirmation");
        Thread.Sleep(600);
        Ui.Click(confirm, Ui.WaitElement(confirm, "Yes", ControlType.Button), 400);
        Thread.Sleep(1400);

        Say("Mei", "Found four copies of the same résumé.", Mei, 0);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Duplicates", ControlType.RadioButton), 450);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Scan", ControlType.Button), 450);
        Ui.WaitElement(_hub, "Select all but the oldest copy", ControlType.Button, timeoutMs: 30000);
        Thread.Sleep(2000);

        Say("Sam", "Saved straight into Invoices. From a Save dialog.", Sam, 0);
        Ui.Place(_hub, 960, 12, 945, 1015); // make room for Sam's notes app
        SaveDialogJump("invoices", 1700);
        Ui.Place(_hub, 280, 12, 1360, 1015);

        Say("Riya", "Oops. Undo.", Riya, 0);
        Ui.Click(_hub, Ui.WaitElement(_hub, "History", ControlType.ListItem), 450);
        Thread.Sleep(500);
        Ui.Click(_hub, Ui.WaitElement(_hub, "Undo", ControlType.Button), 450);
        Thread.Sleep(1500);

        Say("Leo", "It already knows my favourite folders.", Leo, 0);
        Hotkey(); // an empty Quick Jump lists the folders it has learned
        Thread.Sleep(2300);
        ShowKeys("Esc");
        Press(Escape);
        Thread.Sleep(500);
        OnUi(overlay.HideSay);

        // ── Punchline ──
        Say("Mei", "We all did the same thing, didn't we?", Mei, 2100);
        Say("Leo", "Yep.", Leo, 1100);
        Say("Sam", "Can it find my motivation?", Sam, 1700);
        Hotkey();
        Type("motivation", 95);
        Thread.Sleep(2600); // Quick Jump: "No matches."
        ShowKeys("Esc");
        Press(Escape);
        Say("Riya", "It finds files, Sam.", Riya, 2100);
        OnUi(overlay.HideSay);
        Thread.Sleep(300);

        OnUi(() => overlay.Card("Zoomies", "Find it. Organize it. Undo anything.", "Free for Windows   ·   Private   ·   Light"));
        Thread.Sleep(4800);
    }

    /// <summary>A speech bubble; <paramref name="holdMs"/> is how long to wait before the next step (0 = carry on while it shows).</summary>
    private void Say(string name, string line, string color, int holdMs)
    {
        OnUi(() => overlay.Say(name, line, color));
        if (holdMs > 0) Thread.Sleep(holdMs);
    }

    private void Beat(string text, int holdMs)
    {
        OnUi(() => { overlay.HideSay(); overlay.Beat(text); });
        Thread.Sleep(holdMs);
        OnUi(overlay.HideBeat);
        Thread.Sleep(350);
    }
}
