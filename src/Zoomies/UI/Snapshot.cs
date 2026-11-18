#if DEBUG
// Developer tool, left out of release builds.
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zoomies.Core.Indexing;

namespace Zoomies.UI;

/// <summary>
/// Developer tool: "Zoomies.exe --snapshot &lt;folder&gt; [--dark]" renders every screen to PNG files and exits.
/// Used for visual review and store screenshots. It reads the user's folders but never changes anything.
/// </summary>
internal static class Snapshot
{
    public static async void Run(App app, string outputDir, bool dark)
    {
        try
        {
            Directory.CreateDirectory(outputDir);
            app.InitializeCore();
            Themes.ThemeManager.Apply(dark ? Themes.ThemeManager.Midnight : Themes.ThemeManager.Sand);
            string suffix = dark ? "dark" : "light";

            app.ApplyIndexSettings(TimeSpan.Zero);
            for (int i = 0; i < 600 && app.Index.State != IndexState.Ready; i++) await Task.Delay(100);

            var hub = new HubWindow { Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
            hub.Show();

            // Organize uses a demo folder so screenshots never show the user's own files.
            hub.Navigate("organize", Core.KnownFolders.IsDemo ? Core.KnownFolders.Downloads : CreateDemoDownloads());
            await Task.Delay(1500);
            if (hub.PageHost.Content is Pages.OrganizePage organize)
            {
                organize.SimulateRename("stl", ".stl", "3D Prints");
                await Task.Delay(800);
                SaveFullPage(organize, Path.Combine(outputDir, $"organize-full-{suffix}.png"));
            }
            Save((FrameworkElement)hub.Content, Path.Combine(outputDir, $"organize-{suffix}.png"));

            (string Page, string? Arg, int WaitMs)[] pages =
            [
                ("home", null, 9000), ("cleanup", null, 600),
                ("duplicates", null, 9000), ("workspaces", null, 800), ("history", null, 600), ("settings", null, 800),
            ];
            foreach (var (page, arg, wait) in pages)
            {
                hub.Navigate(page, arg);
                await Task.Delay(wait);
                Save((FrameworkElement)hub.Content, Path.Combine(outputDir, $"{page}-{suffix}.png"));
            }
            hub.Close();

            var launcher = new LauncherWindow { Left = -20000, Top = 0, ShowActivated = false };
            launcher.Show();
            await launcher.PrepareSnapshot("doc");
            Save((FrameworkElement)launcher.Content, Path.Combine(outputDir, $"launcher-{suffix}.png"));
            launcher.Close();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDir, "snapshot-error.txt"), ex.ToString());
        }
        app.Shutdown();
    }

    private static string CreateDemoDownloads()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ZoomiesDemo", "Downloads");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var old = DateTime.UtcNow.AddDays(-40);
        string[] names =
        [
            "Resume 2026.pdf", "Lecture notes - week 3.docx", "Budget.xlsx", "Holiday photo.jpg", "Screenshot 2026-09-14.png",
            "phone-bracket.stl", "benchy.stl", "gear-housing.stl", "drone-frame.stl", "keycap.3mf", "song.mp3", "trip.mp4",
            "project-backup.zip", "VSCodeUserSetup-x64.exe", "python-3.13.2-amd64.exe", "data.csv", "mystery.xyz",
        ];
        foreach (string name in names)
        {
            string file = Path.Combine(dir, name);
            File.WriteAllText(file, new string('x', 2048 + name.Length * 997));
            File.SetLastWriteTimeUtc(file, old);
        }
        return dir;
    }

    /// <summary>Renders a page's whole scrollable content, not just the part that fits in the window.</summary>
    private static void SaveFullPage(System.Windows.Controls.UserControl page, string file)
    {
        if (page.Content is not System.Windows.Controls.ScrollViewer { Content: FrameworkElement content }) return;
        content.UpdateLayout();
        var size = new Size(content.ActualWidth + content.Margin.Left + content.Margin.Right,
                            content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)page.FindResource("Brush.Background"), null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(new Point(content.Margin.Left, content.Margin.Top), new Size(content.ActualWidth, content.ActualHeight)));
        }
        var dpi = VisualTreeHelper.GetDpi(page);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    private static void Save(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
#endif
