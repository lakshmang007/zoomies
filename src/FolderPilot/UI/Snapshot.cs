using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderPilot.Core.Indexing;

namespace FolderPilot.UI;

/// <summary>
/// Developer tool: "FolderPilot.exe --snapshot &lt;folder&gt; [--dark]" renders every screen to PNG files and exits.
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
            Themes.ThemeManager.Apply(forceDark: dark);
            string suffix = dark ? "dark" : "light";

            app.ApplyIndexSettings(TimeSpan.Zero);
            for (int i = 0; i < 600 && app.Index.State != IndexState.Ready; i++) await Task.Delay(100);

            var hub = new HubWindow { Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false };
            hub.Show();
            (string Page, string? Arg, int WaitMs)[] pages =
            [
                ("home", null, 9000), ("organize", Core.KnownFolders.Downloads, 1500), ("cleanup", null, 600),
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
