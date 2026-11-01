using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DemoRecorder;

/// <summary>
/// Captions, a key-press badge and full-screen title cards drawn on top of the demo.
/// They never take focus and let clicks through, so they can't disturb what is being recorded.
/// </summary>
internal sealed class Overlay
{
    private readonly Window _caption;
    private readonly TextBlock _title = Text(28, FontWeights.SemiBold, "#FFFFFF");
    private readonly TextBlock _subtitle = Text(18, FontWeights.Normal, "#CBD5E1");
    private readonly Window _keys;
    private readonly TextBlock _keysText = Text(22, FontWeights.SemiBold, "#FFFFFF");
    private readonly Window _card;
    private readonly TextBlock _cardTitle = Text(72, FontWeights.Bold, "#FFFFFF");
    private readonly TextBlock _cardTagline = Text(30, FontWeights.Normal, "#E2E8F0");
    private readonly TextBlock _cardFooter = Text(22, FontWeights.SemiBold, "#A9BBFF");

    public Overlay(int screenWidth, int screenHeight, string? logoPath)
    {
        _subtitle.Margin = new Thickness(0, 4, 0, 0);
        _caption = Layer(new Border
        {
            Background = Brush("#EB0F172A"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(30, 16, 30, 18),
            Child = new StackPanel { Children = { _title, _subtitle } },
        }, screenWidth, screenHeight, bottom: 96);

        _keys = Layer(new Border
        {
            Background = Brush("#F21D4ED8"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 8, 18, 9),
            Child = _keysText,
        }, screenWidth, screenHeight, bottom: 200);

        var cardContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (logoPath is not null && File.Exists(logoPath))
            cardContent.Children.Add(new Image { Source = new BitmapImage(new Uri(logoPath)), Width = 140, Height = 140, Margin = new Thickness(0, 0, 0, 24) });
        _cardTitle.HorizontalAlignment = _cardTagline.HorizontalAlignment = _cardFooter.HorizontalAlignment = HorizontalAlignment.Center;
        _cardTagline.Margin = new Thickness(0, 14, 0, 0);
        _cardTagline.TextAlignment = TextAlignment.Center;
        _cardTagline.MaxWidth = screenWidth * 0.7;
        _cardTagline.TextWrapping = TextWrapping.Wrap;
        _cardFooter.Margin = new Thickness(0, 34, 0, 0);
        cardContent.Children.Add(_cardTitle);
        cardContent.Children.Add(_cardTagline);
        cardContent.Children.Add(_cardFooter);
        _card = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
            Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
            Left = 0, Top = 0, Width = screenWidth, Height = screenHeight,
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#F705060A"), (Color)ColorConverter.ConvertFromString("#F70E1426"), 35),
            Content = cardContent, Opacity = 0,
        };
        MakeClickThrough(_card);

        // Speech bubble for the ad: coloured avatar with the speaker's initial, their name, and the line.
        _sayInitial.HorizontalAlignment = HorizontalAlignment.Center;
        _sayInitial.VerticalAlignment = VerticalAlignment.Center;
        _sayLine.MaxWidth = screenWidth * 0.55;
        _sayLine.TextWrapping = TextWrapping.Wrap;
        var bubbleText = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bubbleText.Children.Add(_sayName);
        bubbleText.Children.Add(_sayLine);
        var bubble = new StackPanel { Orientation = Orientation.Horizontal };
        bubble.Children.Add(_sayAvatar);
        bubble.Children.Add(bubbleText);
        _sayAvatar.Child = _sayInitial;
        _say = Layer(new Border
        {
            Background = Brush("#F00F131D"),
            BorderBrush = Brush("#33FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16, 14, 30, 16),
            Child = bubble,
        }, screenWidth, screenHeight, bottom: 96);

        // Full-screen "beat" for the big reaction moments ("Impossible.").
        _beatText.HorizontalAlignment = _beatSub.HorizontalAlignment = HorizontalAlignment.Center;
        _beatText.TextAlignment = _beatSub.TextAlignment = TextAlignment.Center;
        _beatText.MaxWidth = _beatSub.MaxWidth = screenWidth * 0.75;
        _beatText.TextWrapping = _beatSub.TextWrapping = TextWrapping.Wrap;
        _beatSub.Margin = new Thickness(0, 18, 0, 0);
        _beat = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize,
            Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
            Left = 0, Top = 0, Width = screenWidth, Height = screenHeight,
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FA05060A"), (Color)ColorConverter.ConvertFromString("#FA0E1426"), 35),
            Content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { _beatText, _beatSub } },
            Opacity = 0,
        };
        MakeClickThrough(_beat);
    }

    private readonly Window _say;
    private readonly Border _sayAvatar = new() { Width = 48, Height = 48, CornerRadius = new CornerRadius(24) };
    private readonly TextBlock _sayInitial = Text(22, FontWeights.Bold, "#0A0F1F");
    private readonly TextBlock _sayName = Text(16, FontWeights.SemiBold, "#A9BBFF");
    private readonly TextBlock _sayLine = Text(27, FontWeights.SemiBold, "#FFFFFF");
    private readonly Window _beat;
    private readonly TextBlock _beatText = Text(84, FontWeights.Bold, "#FFFFFF");
    private readonly TextBlock _beatSub = Text(30, FontWeights.Normal, "#A9BBFF");

    /// <summary>A character's line, shown as a speech bubble with their avatar.</summary>
    public void Say(string name, string line, string color)
    {
        HideCaption();
        _sayAvatar.Background = Brush(color);
        _sayInitial.Text = name[..1];
        _sayName.Text = name;
        _sayName.Foreground = Brush(color);
        _sayLine.Text = line;
        FadeIn(_say);
    }

    public void HideSay() => FadeOut(_say);

    /// <summary>A full-screen line for the big moments.</summary>
    public void Beat(string text, string sub = "")
    {
        _beatText.Text = text;
        _beatSub.Text = sub;
        _beatSub.Visibility = sub.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FadeIn(_beat);
    }

    public void HideBeat() => FadeOut(_beat);

    public void Caption(string title, string subtitle = "")
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
        _subtitle.Visibility = subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FadeIn(_caption);
    }

    public void HideCaption() => FadeOut(_caption);

    /// <summary>Shows which keys are being pressed, since viewers can't see the keyboard.</summary>
    public void Keys(string keys)
    {
        _keysText.Text = keys;
        FadeIn(_keys);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        timer.Tick += (_, _) => { timer.Stop(); if (_keysText.Text == keys) FadeOut(_keys); };
        timer.Start();
    }

    public void Card(string title, string tagline, string footer)
    {
        _cardTitle.Text = title;
        _cardTagline.Text = tagline;
        _cardFooter.Text = footer;
        HideCaption();
        FadeIn(_card);
    }

    public void HideCard() => FadeOut(_card);

    public void CloseAll()
    {
        _caption.Close();
        _keys.Close();
        _card.Close();
        _say.Close();
        _beat.Close();
        foreach (var w in _extra.ToList()) w.Close();
        _extra.Clear();
    }

    private readonly List<Window> _extra = [];

    /// <summary>
    /// A plain backdrop over the user's wallpaper and desktop widgets, placed directly beneath
    /// <paramref name="below"/> so the demo windows stay on top of it.
    /// </summary>
    public void ShowStage(int screenWidth, int screenHeight, IntPtr below)
    {
        var stage = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Left = 0, Top = 0, Width = screenWidth, Height = screenHeight,
            Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FF05060A"), (Color)ColorConverter.ConvertFromString("#FF0E1426"), 35),
        };
        stage.SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(stage).Handle;
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x08000000 | 0x80); // never takes focus
        };
        stage.Show();
        IntPtr handle = new WindowInteropHelper(stage).Handle;
        SetWindowPos(handle, below, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE | NOMOVE | NOACTIVATE
        _extra.Add(stage);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static Window Layer(FrameworkElement content, int screenWidth, int screenHeight, int bottom)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
            SizeToContent = SizeToContent.WidthAndHeight, Content = content, Opacity = 0,
        };
        // Center horizontally once the size is known.
        window.SizeChanged += (_, _) =>
        {
            window.Left = (screenWidth - window.ActualWidth) / 2;
            window.Top = screenHeight - bottom - window.ActualHeight;
        };
        MakeClickThrough(window);
        return window;
    }

    private static void MakeClickThrough(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x08000000 | 0x80);
        };
    }

    private static void FadeIn(Window window)
    {
        if (!window.IsVisible) window.Show();
        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
    }

    private static void FadeOut(Window window) =>
        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));

    private static TextBlock Text(double size, FontWeight weight, string color) => new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontSize = size, FontWeight = weight, Foreground = Brush(color),
    };

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
