using System.Collections;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FolderPilot.Interop;
using Microsoft.Win32;

namespace FolderPilot.Themes;

/// <summary>
/// Applies FolderPilot's look: Sand (light) or Midnight (dark), chosen by the user or following Windows,
/// plus high contrast when Windows asks for it. Updates live.
/// </summary>
internal static class ThemeManager
{
    public const string FollowWindows = "system";
    public const string Sand = "sand";
    public const string Midnight = "midnight";

    private const string PaletteMarker = "FolderPilot.Palette";
    private static string _choice = FollowWindows;
    private static readonly List<object> AccentOverrides = [];

    public static bool IsDark { get; private set; }
    public static string Choice => _choice;

    /// <param name="choice">"system", "sand" or "midnight"; null re-applies the current choice (e.g. after Windows changed).</param>
    public static void Apply(string? choice = null)
    {
        if (choice is FollowWindows or Sand or Midnight) _choice = choice;
        var app = Application.Current;
        IsDark = _choice == Midnight || (_choice == FollowWindows && SystemIsDark());
        bool highContrast = SystemParameters.HighContrast;

        ResourceDictionary palette = highContrast
            ? HighContrastPalette()
            : new ResourceDictionary { Source = new Uri($"pack://application:,,,/FolderPilot;component/Themes/{(IsDark ? "Midnight" : "Sand")}.xaml") };
        palette[PaletteMarker] = true;

        var dictionaries = app.Resources.MergedDictionaries;
        int index = -1;
        for (int i = 0; i < dictionaries.Count; i++)
            if (dictionaries[i].Contains(PaletteMarker)) { index = i; break; }
        if (index >= 0) dictionaries[index] = palette;
        else dictionaries.Insert(0, palette);

        // The built-in controls follow our choice rather than the Windows setting.
        app.ThemeMode = highContrast ? ThemeMode.System : IsDark ? ThemeMode.Dark : ThemeMode.Light;
        ApplyAccent(app.Resources, highContrast ? null : (Color)palette["Color.Accent"]);
        foreach (Window w in app.Windows) StyleWindow(w);
    }

    /// <summary>Dark or light title bar, Windows 11 rounded corners, and a title bar tinted to the theme.</summary>
    public static void StyleWindow(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        if (SystemParameters.HighContrast) return;
        // Ignored on Windows 10, which has no caption colour attribute.
        if (Application.Current.TryFindResource("Color.Caption") is Color caption)
        {
            int c = ToColorRef(caption);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref c, sizeof(int));
        }
        if (Application.Current.TryFindResource("Color.CaptionText") is Color text)
        {
            int c = ToColorRef(text);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TEXT_COLOR, ref c, sizeof(int));
        }
    }

    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// The built-in controls (checkboxes, radio buttons, focus lines, progress bars…) are coloured from the Windows
    /// accent colour. Every resource that equals one of the Windows accent shades is overridden with the matching shade
    /// of the theme's own accent, so the whole app uses one accent whatever colour the user picked in Windows.
    /// </summary>
    private static void ApplyAccent(ResourceDictionary appResources, Color? accent)
    {
        foreach (object key in AccentOverrides) appResources.Remove(key);
        AccentOverrides.Clear();
        if (accent is not { } a) return;

        Color[] system =
        [
            SystemColors.AccentColor, SystemColors.AccentColorLight1, SystemColors.AccentColorLight2, SystemColors.AccentColorLight3,
            SystemColors.AccentColorDark1, SystemColors.AccentColorDark2, SystemColors.AccentColorDark3,
        ];
        Color[] ours =
        [
            a, Mix(a, Colors.White, 0.25), Mix(a, Colors.White, 0.5), Mix(a, Colors.White, 0.75),
            Mix(a, Colors.Black, 0.2), Mix(a, Colors.Black, 0.4), Mix(a, Colors.Black, 0.6),
        ];
        object[] colorKeys =
        [
            SystemColors.AccentColorKey, SystemColors.AccentColorLight1Key, SystemColors.AccentColorLight2Key, SystemColors.AccentColorLight3Key,
            SystemColors.AccentColorDark1Key, SystemColors.AccentColorDark2Key, SystemColors.AccentColorDark3Key,
        ];
        object[] brushKeys =
        [
            SystemColors.AccentColorBrushKey, SystemColors.AccentColorLight1BrushKey, SystemColors.AccentColorLight2BrushKey, SystemColors.AccentColorLight3BrushKey,
            SystemColors.AccentColorDark1BrushKey, SystemColors.AccentColorDark2BrushKey, SystemColors.AccentColorDark3BrushKey,
        ];

        void Override(object key, object value)
        {
            appResources[key] = value;
            AccentOverrides.Add(key);
        }

        for (int i = 0; i < ours.Length; i++)
        {
            Override(colorKeys[i], ours[i]);
            Override(brushKeys[i], Frozen(new SolidColorBrush(ours[i])));
        }

        foreach (var (key, value) in FluentEntries(appResources))
        {
            int shade = value switch
            {
                SolidColorBrush b => Array.FindIndex(system, s => SameRgb(s, b.Color)),
                Color c => Array.FindIndex(system, s => SameRgb(s, c)),
                _ => -1,
            };
            if (shade < 0) continue;
            Override(key, value switch
            {
                SolidColorBrush b => Frozen(new SolidColorBrush(Color.FromArgb(b.Color.A, ours[shade].R, ours[shade].G, ours[shade].B)) { Opacity = b.Opacity }),
                Color c => Color.FromArgb(c.A, ours[shade].R, ours[shade].G, ours[shade].B),
                _ => value,
            });
        }
    }

    private static IEnumerable<(object Key, object Value)> FluentEntries(ResourceDictionary root)
    {
        foreach (var dictionary in root.MergedDictionaries)
        {
            if (dictionary.Contains(PaletteMarker)) continue;
            foreach (var entry in Walk(dictionary)) yield return entry;
        }
    }

    private static IEnumerable<(object Key, object Value)> Walk(ResourceDictionary dictionary)
    {
        foreach (DictionaryEntry e in dictionary)
            if (e.Value is SolidColorBrush or Color) yield return (e.Key, e.Value);
        foreach (var merged in dictionary.MergedDictionaries)
            foreach (var entry in Walk(merged)) yield return entry;
    }

    private static bool SameRgb(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>High-contrast themes are an accessibility need, so every color comes from the user's chosen system colors.</summary>
    private static ResourceDictionary HighContrastPalette()
    {
        var d = new ResourceDictionary();
        void Set(string key, object brush) => d[key] = brush;
        Set("Brush.Background", SystemColors.WindowBrush);
        Set("Brush.Sidebar", SystemColors.WindowBrush);
        Set("Brush.Inset", SystemColors.WindowBrush);
        Set("Brush.Card", SystemColors.WindowBrush);
        Set("Brush.Launcher", SystemColors.WindowBrush);
        Set("Brush.Input", SystemColors.WindowBrush);
        foreach (var key in new[] { "Brush.CardBorder", "Brush.Divider", "Brush.InputBorder", "Brush.LauncherBorder", "Brush.SwitchOff" })
            Set(key, SystemColors.WindowTextBrush);
        foreach (var key in new[] { "Brush.Text", "Brush.TextSecondary", "Brush.TextTertiary", "Brush.AccentText", "Brush.Danger", "Brush.Success", "Brush.Warning" })
            Set(key, SystemColors.WindowTextBrush);
        foreach (var key in new[] { "Brush.Accent", "Brush.AccentHover", "Brush.AccentPressed", "Brush.Selected", "Brush.DangerHover" })
            Set(key, SystemColors.HighlightBrush);
        Set("Brush.OnAccent", SystemColors.HighlightTextBrush);
        foreach (var key in new[] { "Brush.AccentSoft", "Brush.DangerSoft", "Brush.SuccessSoft", "Brush.WarningSoft", "Brush.Hover", "Brush.Pressed" })
            Set(key, SystemColors.WindowBrush);
        return d;
    }
}
