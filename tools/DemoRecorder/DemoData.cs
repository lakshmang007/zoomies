using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zoomies.Core.Frecency;
using Zoomies.Core.Settings;
using Zoomies.Core.Storage;

namespace DemoRecorder;

/// <summary>
/// Builds a believable student's PC under one folder: a messy Downloads, college work, trip photos,
/// old lecture recordings. Images and PDFs are real so File Explorer shows proper thumbnails.
/// </summary>
internal static class DemoData
{
    public const string Marker = ".demo-root";
    private static readonly DateTime Now = DateTime.UtcNow;

    public static void Create(string root)
    {
        if (Directory.Exists(root))
        {
            // Only ever delete a folder this tool created.
            if (!File.Exists(Path.Combine(root, Marker)))
                throw new InvalidOperationException($"{root} already exists and is not a demo folder. Pick another path.");
            Directory.Delete(root, recursive: true);
        }
        Directory.CreateDirectory(root);
        string marker = Path.Combine(root, Marker);
        File.WriteAllText(marker, "Zoomies demo data. Safe to delete.");
        File.SetAttributes(marker, FileAttributes.Hidden);

        string P(string relative) => Path.Combine(root, relative);

        // Downloads: loose files of every kind, the thing Zoomies tidies.
        Pdf(P(@"Downloads\Lecture 12 - Neural Networks.pdf"), "Lecture 12: Neural Networks", 3);
        Pdf(P(@"Downloads\Internship offer letter.pdf"), "Offer of Internship", 6);
        byte[] resume = PdfBytes("Resume - Software Engineer");
        Write(P(@"Downloads\Resume.pdf"), resume, 20);
        Image(P(@"Downloads\Hackathon poster.png"), 1600, 900, "#7C3AED", "#EC4899", "HACK NIGHT 2026", 9);
        Photo(P(@"Downloads\IMG_20251012_184233.jpg"), "#F59E0B", "#0EA5E9", 18);
        Image(P(@"Downloads\Screenshot 2026-10-21 101455.png"), 1600, 900, "#E5E7EB", "#CBD5E1", "Screenshot", 10, dark: true);
        Image(P(@"Downloads\Untitled design.png"), 1080, 1080, "#10B981", "#065F46", "Club logo v2", 1);
        Bytes(P(@"Downloads\phone-stand.stl"), 2_300_000, 4);
        Bytes(P(@"Downloads\benchy.stl"), 5_100_000, 12);
        Bytes(P(@"Downloads\cable-clip.stl"), 640_000, 7);
        Bytes(P(@"Downloads\gear-box.3mf"), 1_800_000, 15);
        Bytes(P(@"Downloads\Project report final v3.docx"), 1_250_000, 2);
        Bytes(P(@"Downloads\marks-sheet.xlsx"), 48_000, 30);
        Bytes(P(@"Downloads\dataset.csv"), 2_900_000, 5);
        Bytes(P(@"Downloads\Lo-fi beats.mp3"), 4_200_000, 40);
        Bytes(P(@"Downloads\Trip vlog.mp4"), 24_000_000, 22);
        Bytes(P(@"Downloads\assignment-3.zip"), 3_400_000, 8);
        Bytes(P(@"Downloads\Roboto.zip"), 1_100_000, 33);
        Bytes(P(@"Downloads\VSCodeUserSetup-x64.exe"), 92_000_000, 60);
        Bytes(P(@"Downloads\python-3.13.2-amd64.exe"), 27_000_000, 75);
        Bytes(P(@"Downloads\node-v22.14.0-x64.msi"), 30_000_000, 90);
        Clutter(P("Downloads"));

        // The same résumé saved four times under different names: what the duplicate finder is for.
        byte[] resumeV2 = new byte[1_300_000];
        new Random(44).NextBytes(resumeV2);
        foreach (var (name, days) in new[] { ("Resume - final.pdf", 40), ("Resume - final (1).pdf", 38), ("Resume - FINAL final.pdf", 31), ("Resume - use this one.pdf", 25) })
            Write(P($@"Downloads\{name}"), resumeV2, days);

        // Desktop
        Text(P(@"Desktop\todo.txt"), "- finish report\n- submit assignment 3\n- print poster\n", 1);
        Text(P(@"Desktop\notes.txt"), "Viva questions: CNN vs RNN, overfitting, dropout\n", 4);
        Photo(P(@"Desktop\meme.jpg"), "#F97316", "#FACC15", 12);

        // Documents: college work, invoices, side projects, an old semester nobody opens any more.
        Write(P(@"Documents\Resume.pdf"), resume, 20);
        Bytes(P(@"Documents\College\Final Year Project\Synopsis.docx"), 380_000, 10);
        Pdf(P(@"Documents\College\Final Year Project\Literature survey.pdf"), "Literature Survey", 14);
        Bytes(P(@"Documents\College\Final Year Project\Final presentation.pptx"), 6_400_000, 3);
        Bytes(P(@"Documents\College\Final Year Project\Report draft.docx"), 1_100_000, 2);
        Pdf(P(@"Documents\College\Semester 7\Machine Learning notes.pdf"), "Machine Learning - Unit 3", 12);
        Bytes(P(@"Documents\College\Semester 7\DBMS assignment.docx"), 220_000, 25);
        Bytes(P(@"Documents\College\Semester 5\Microprocessors notes.pdf"), 8_200_000, 430, accessedDaysAgo: 430);
        Bytes(P(@"Documents\College\Semester 5\OS lab record.docx"), 3_100_000, 445, accessedDaysAgo: 445);
        Bytes(P(@"Documents\College\Semester 5\Mini project.zip"), 45_000_000, 410, accessedDaysAgo: 410);
        Bytes(P(@"Documents\Old projects backup.zip"), 31_000_000, 480, accessedDaysAgo: 480);
        foreach (string month in new[] { "08", "09", "10" })
            Pdf(P($@"Documents\Invoices\Invoice-2026-{month}.pdf"), $"Invoice 2026-{month}", 70 - int.Parse(month) * 5);
        Text(P(@"Documents\Projects\Portfolio website\index.html"), "<!doctype html><title>Portfolio</title>", 2);
        Text(P(@"Documents\Projects\Portfolio website\style.css"), "body { font-family: system-ui; }", 2);
        Text(P(@"Documents\Projects\Weather app\app.py"), "print('weather')\n", 16);
        Text(P(@"Documents\Projects\Weather app\README.md"), "# Weather app\n", 16);
        Directory.CreateDirectory(P(@"Documents\New folder"));

        // Pictures: recently viewed, so they are never suggested for cleanup.
        string[] beach = ["#0EA5E9", "#F59E0B", "#22D3EE", "#FB7185", "#34D399", "#F472B6"];
        for (int i = 0; i < beach.Length; i++)
            Photo(P($@"Pictures\Trip to Goa 2025\IMG_00{11 + i}.jpg"), beach[i], "#FDE68A", 340, accessedDaysAgo: 9);
        Image(P(@"Pictures\Screenshots\Screenshot 2026-10-02.png"), 1600, 900, "#1E293B", "#334155", "Dashboard", 27);
        Image(P(@"Pictures\Screenshots\Screenshot 2026-10-15.png"), 1600, 900, "#0F172A", "#1D4ED8", "Error log", 14);
        Image(P(@"Pictures\Screenshots\Screenshot 2026-10-24.png"), 1600, 900, "#F8FAFC", "#E2E8F0", "Results table", 5, dark: true);
        Image(P(@"Pictures\Wallpapers\Mountains.png"), 1920, 1080, "#1E3A8A", "#F97316", "", 120, accessedDaysAgo: 2);

        // Videos and music: old recordings nobody has opened in over a year.
        Bytes(P(@"Videos\Old lecture recordings\Lecture 01 - Introduction.mp4"), 64_000_000, 420, accessedDaysAgo: 420);
        Bytes(P(@"Videos\Old lecture recordings\Lecture 02 - Basics.mp4"), 61_000_000, 418, accessedDaysAgo: 418);
        Bytes(P(@"Videos\Screen recording 2025-03-02.mp4"), 38_000_000, 520, accessedDaysAgo: 520);
        Bytes(P(@"Music\Playlist\Track 01.mp3"), 5_100_000, 200, accessedDaysAgo: 3);
        Bytes(P(@"Music\Playlist\Track 02.mp3"), 4_700_000, 200, accessedDaysAgo: 3);

        WriteAppData(root);
        RestoreAccessTimes();
    }

    /// <summary>
    /// The antivirus reads every new file and that resets its "last opened" date to now, which would hide the
    /// old files from the "unused" scan. Wait for the scan to finish, then put the intended dates back.
    /// </summary>
    private static void RestoreAccessTimes()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 5000 : 2500);
            int wrong = 0;
            foreach (var (path, accessed) in AccessTimes)
            {
                if (Math.Abs((File.GetLastAccessTimeUtc(path) - accessed).TotalMinutes) < 5) continue;
                File.SetLastAccessTimeUtc(path, accessed);
                wrong++;
            }
            if (wrong == 0) return;
        }
    }

    private static readonly List<(string Path, DateTime Accessed)> AccessTimes = [];

    /// <summary>Gives the demo copy of Zoomies a Quick Jump shortcut that no other app on this PC is using, and the theme to film.</summary>
    public static void SetHotkey(string root, string hotkey, string theme) =>
        JsonFile.Write(Path.Combine(root, ".zoomies", "settings.json"),
            new AppSettings { HasCompletedWelcome = true, Hotkey = hotkey, Theme = theme }, CoreJsonContext.Default.AppSettings);

    /// <summary>Settings and "learned" folders for the demo copy of Zoomies, as if it had been used for a few weeks.</summary>
    private static void WriteAppData(string root)
    {
        string data = Path.Combine(root, ".zoomies");
        Directory.CreateDirectory(data);
        File.SetAttributes(data, FileAttributes.Directory | FileAttributes.Hidden);
        JsonFile.Write(Path.Combine(data, "settings.json"), new AppSettings { HasCompletedWelcome = true }, CoreJsonContext.Default.AppSettings);

        var store = new FrecencyStore(Path.Combine(data, "folders.json"));
        void Visit(string relative, int times, double hoursAgo)
        {
            for (int i = 0; i < times; i++) store.Visit(Path.Combine(root, relative), whenUtc: Now.AddHours(-hoursAgo - i));
        }
        Visit(@"Documents\College\Final Year Project", 14, 1);
        Visit("Downloads", 18, 2);
        Visit(@"Documents\College\Semester 7", 8, 26);
        Visit(@"Documents\Projects\Portfolio website", 6, 50);
        Visit(@"Pictures\Trip to Goa 2025", 5, 80);
        Visit(@"Documents\Invoices", 3, 150);
        Visit("Desktop", 4, 30);
        store.SaveIfDirty();
    }

    /// <summary>
    /// A few hundred small files of the kind that pile up in a real Downloads folder: phone photos, screenshots,
    /// "(1)" copies, receipts, setup files. Small, so creating them takes seconds.
    /// </summary>
    private static void Clutter(string downloads)
    {
        var random = new Random(2026);
        string[] colors = ["#F59E0B", "#0EA5E9", "#22C55E", "#EC4899", "#8B5CF6", "#F97316", "#14B8A6", "#EF4444"];
        for (int i = 0; i < 70; i++)
        {
            var day = Now.AddDays(-random.Next(15, 300));
            string name = $"IMG_{day:yyyyMMdd}_{random.Next(100000, 999999)}.jpg";
            Render(Path.Combine(downloads, name), 240, 180, dc =>
            {
                dc.DrawRectangle(new LinearGradientBrush(Hex(colors[random.Next(colors.Length)]), Hex("#FFF7ED"), 90), null, new Rect(0, 0, 240, 180));
                dc.DrawEllipse(new SolidColorBrush(Hex(colors[random.Next(colors.Length)])), null, new Point(random.Next(40, 200), 60), 26, 26);
            }, random.Next(15, 300), null);
        }
        for (int i = 0; i < 45; i++)
        {
            var day = Now.AddDays(-random.Next(12, 200));
            Render(Path.Combine(downloads, $"Screenshot {day:yyyy-MM-dd} {random.Next(100000, 235959)}.png"), 320, 180, dc =>
                dc.DrawRectangle(new LinearGradientBrush(Hex("#1E293B"), Hex(colors[random.Next(colors.Length)]), 20), null, new Rect(0, 0, 320, 180)),
                random.Next(12, 200), null);
        }
        string[] docs = ["notes", "assignment", "lab manual", "question paper", "syllabus", "receipt", "ticket", "form", "certificate", "brochure"];
        for (int i = 0; i < 70; i++)
        {
            string stem = docs[random.Next(docs.Length)];
            string name = random.Next(3) switch { 0 => $"{stem} ({random.Next(1, 9)}).pdf", 1 => $"{stem}-{random.Next(2, 99)}.pdf", _ => $"{stem}_{random.Next(1000, 9999)}.docx" };
            string path = Path.Combine(downloads, name);
            if (!File.Exists(path)) Bytes(path, random.Next(20_000, 400_000), random.Next(15, 280));
        }
        string[] other = ["song", "voice note", "recording"];
        for (int i = 0; i < 30; i++)
            Bytes(Path.Combine(downloads, $"{other[random.Next(other.Length)]} {random.Next(1, 999)}.mp3"), random.Next(200_000, 900_000), random.Next(20, 250));
        for (int i = 0; i < 40; i++)
        {
            string path = Path.Combine(downloads, $"{docs[random.Next(docs.Length)]}-{random.Next(100, 999)}.zip");
            if (!File.Exists(path)) Bytes(path, random.Next(50_000, 600_000), random.Next(20, 250));
        }
        for (int i = 0; i < 25; i++)
            Bytes(Path.Combine(downloads, $"setup-{random.Next(10, 99)}.{random.Next(1, 9)}.exe"), random.Next(300_000, 900_000), random.Next(20, 120));
    }

    // ───────────────────────────── file helpers ─────────────────────────────

    private static void Write(string path, byte[] content, int modifiedDaysAgo, int? accessedDaysAgo = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        Stamp(path, modifiedDaysAgo, accessedDaysAgo);
    }

    /// <summary>Filler content of a realistic size. Seeded by name, so no two files are accidental duplicates.</summary>
    private static void Bytes(string path, long size, int modifiedDaysAgo, int? accessedDaysAgo = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var random = new Random(path.GetHashCode(StringComparison.OrdinalIgnoreCase));
        var block = new byte[1 << 20];
        using (var stream = File.Create(path))
        {
            for (long written = 0; written < size; written += block.Length)
            {
                random.NextBytes(block);
                stream.Write(block, 0, (int)Math.Min(block.Length, size - written));
            }
        }
        Stamp(path, modifiedDaysAgo, accessedDaysAgo);
    }

    private static void Text(string path, string text, int modifiedDaysAgo) =>
        Write(path, Encoding.UTF8.GetBytes(text), modifiedDaysAgo);

    private static void Stamp(string path, int modifiedDaysAgo, int? accessedDaysAgo)
    {
        var modified = Now.AddDays(-modifiedDaysAgo).AddMinutes(-modifiedDaysAgo * 37 % 600);
        File.SetCreationTimeUtc(path, modified);
        File.SetLastWriteTimeUtc(path, modified);
        var accessed = Now.AddDays(-(accessedDaysAgo ?? Math.Min(modifiedDaysAgo, 7)));
        File.SetLastAccessTimeUtc(path, accessed);
        AccessTimes.Add((path, accessed));
    }

    private static void Pdf(string path, string title, int modifiedDaysAgo) => Write(path, PdfBytes(title), modifiedDaysAgo);

    /// <summary>A minimal, valid one-page PDF with a title line.</summary>
    private static byte[] PdfBytes(string title)
    {
        string content = $"BT /F1 28 Tf 72 700 Td ({title.Replace("(", "\\(").Replace(")", "\\)")}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>A simple "photo": sky gradient, sun and hills. Enough to look like a picture in a thumbnail.</summary>
    private static void Photo(string path, string sky, string sun, int modifiedDaysAgo, int? accessedDaysAgo = null)
    {
        Render(path, 1600, 1200, dc =>
        {
            dc.DrawRectangle(new LinearGradientBrush(Hex(sky), Hex("#FFF7ED"), 90), null, new Rect(0, 0, 1600, 1200));
            dc.DrawEllipse(new SolidColorBrush(Hex(sun)), null, new Point(1150, 380), 150, 150);
            dc.DrawEllipse(new SolidColorBrush(Hex("#15803D")), null, new Point(400, 1250), 900, 420);
            dc.DrawEllipse(new SolidColorBrush(Hex("#166534")), null, new Point(1300, 1300), 800, 380);
        }, modifiedDaysAgo, accessedDaysAgo);
    }

    private static void Image(string path, int w, int h, string from, string to, string label, int modifiedDaysAgo,
        bool dark = false, int? accessedDaysAgo = null)
    {
        Render(path, w, h, dc =>
        {
            dc.DrawRectangle(new LinearGradientBrush(Hex(from), Hex(to), 35), null, new Rect(0, 0, w, h));
            if (label.Length == 0) return;
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), h / 9.0, dark ? Brushes.Black : Brushes.White, 1.0);
            dc.DrawText(text, new Point(w * 0.07, h * 0.7));
        }, modifiedDaysAgo, accessedDaysAgo);
    }

    private static void Render(string path, int w, int h, Action<DrawingContext> draw, int modifiedDaysAgo, int? accessedDaysAgo)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapEncoder encoder = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? new PngBitmapEncoder()
            : new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        Write(path, stream.ToArray(), modifiedDaysAgo, accessedDaysAgo);
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
