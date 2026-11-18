using System.Windows.Input;

namespace Zoomies.Interop;

/// <summary>A keyboard shortcut such as Ctrl+Alt+Space, parsed from and saved as text.</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    public static Hotkey Default => new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);

    /// <summary>Fallbacks tried in order if the preferred shortcut is taken by another app.</summary>
    public static readonly Hotkey[] Alternatives =
    [
        new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space),
        new(ModifierKeys.Control | ModifierKeys.Shift, Key.Space),
        new(ModifierKeys.Alt | ModifierKeys.Shift, Key.F),
    ];

    public bool IsValid => Key != Key.None && Modifiers != ModifierKeys.None && !IsModifierKey(Key);

    public uint NativeModifiers =>
        ((Modifiers & ModifierKeys.Alt) != 0 ? MOD_ALT : 0)
        | ((Modifiers & ModifierKeys.Control) != 0 ? MOD_CONTROL : 0)
        | ((Modifiers & ModifierKeys.Shift) != 0 ? MOD_SHIFT : 0)
        | ((Modifiers & ModifierKeys.Windows) != 0 ? MOD_WIN : 0)
        | MOD_NOREPEAT;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        ModifierKeys mods = ModifierKeys.None;
        Key key = Key.None;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (!Enum.TryParse(raw, true, out key)) return false;
                    break;
            }
        }
        hotkey = new Hotkey(mods, key);
        return hotkey.IsValid;
    }

    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        Key.Oem3 => "`",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        _ => k.ToString(),
    };
}

/// <summary>Registers one global shortcut with Windows. Costs nothing while idle — no keyboard hook.</summary>
internal sealed class HotkeyRegistration(MessageHost host) : IDisposable
{
    private const int Id = 0x4650;
    private bool _registered;

    public Hotkey? Current { get; private set; }

    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _registered = Native.RegisterHotKey(host.Handle, Id, hotkey.NativeModifiers, hotkey.VirtualKey);
        Current = _registered ? hotkey : null;
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) Native.UnregisterHotKey(host.Handle, Id);
        _registered = false;
        Current = null;
    }

    public void Dispose() => Unregister();
}
