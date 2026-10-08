using System.Windows;
using System.Windows.Interop;
using FolderPilot.Interop;
using Microsoft.Win32;

namespace FolderPilot.Themes;

/// <summary>Follows the Windows light/dark/high-contrast setting, live.</summary>
internal static class ThemeManager
{
    private const string PaletteMarker = "FolderPilot.Palette";
    private static bool? _forced;

    public static bool IsDark { get; private set; }

    public static void Apply(bool? forceDark = null)
    {
        if (forceDark.HasValue) _forced = forceDark;
        var app = Application.Current;
        IsDark = _forced ?? SystemIsDark();

        ResourceDictionary palette = SystemParameters.HighContrast
            ? HighContrastPalette()
            : new ResourceDictionary { Source = new Uri($"pack://application:,,,/FolderPilot;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        palette[PaletteMarker] = true;

        var dictionaries = app.Resources.MergedDictionaries;
        int index = -1;
        for (int i = 0; i < dictionaries.Count; i++)
            if (dictionaries[i].Contains(PaletteMarker)) { index = i; break; }
        if (index >= 0) dictionaries[index] = palette;
        else dictionaries.Insert(0, palette);

        if (_forced.HasValue) app.ThemeMode = _forced.Value ? ThemeMode.Dark : ThemeMode.Light;
        foreach (Window w in app.Windows) StyleWindow(w);
    }

    /// <summary>Dark title bar to match the theme, and Windows 11 rounded corners.</summary>
    public static void StyleWindow(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
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

    /// <summary>High-contrast themes are an accessibility need, so every color comes from the user's chosen system colors.</summary>
    private static ResourceDictionary HighContrastPalette()
    {
        var d = new ResourceDictionary();
        void Set(string key, object brush) => d[key] = brush;
        Set("Brush.Background", SystemColors.WindowBrush);
        Set("Brush.Sidebar", SystemColors.WindowBrush);
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
