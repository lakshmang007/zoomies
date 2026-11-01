using System.Runtime.InteropServices;

namespace DemoRecorder;

/// <summary>Keyboard and mouse input that looks human on video: eased mouse movement and typing with small pauses.</summary>
internal static class Input
{
    public const ushort Ctrl = 0x11, Alt = 0x12, Shift = 0x10, Enter = 0x0D, Escape = 0x1B, Space = 0x20, Tab = 0x09, F4 = 0x73, A = 0x41, S = 0x53;
    private static readonly Random Jitter = new(7);

    public static void Chord(params ushort[] keys)
    {
        foreach (ushort k in keys) Send(KeyInput(k, up: false));
        Thread.Sleep(40);
        for (int i = keys.Length - 1; i >= 0; i--) Send(KeyInput(keys[i], up: true));
        Thread.Sleep(80);
    }

    public static void Press(ushort key) => Chord(key);

    public static void Type(string text, int msPerChar = 85)
    {
        foreach (char c in text)
        {
            Send(UnicodeInput(c, up: false), UnicodeInput(c, up: true));
            Thread.Sleep(msPerChar + Jitter.Next(-25, 35));
        }
    }

    /// <summary>Moves the pointer along an eased path so viewers can follow it.</summary>
    public static void MoveTo(int x, int y, int durationMs = 650)
    {
        GetCursorPos(out var start);
        int steps = Math.Max(1, durationMs / 12);
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // ease in-out
            SetCursorPos((int)(start.X + (x - start.X) * e), (int)(start.Y + (y - start.Y) * e));
            Thread.Sleep(12);
        }
    }

    public static void Click()
    {
        Send(MouseInput(0x0002)); // left down
        Thread.Sleep(60);
        Send(MouseInput(0x0004)); // left up
        Thread.Sleep(120);
    }

    public static void ClickAt(int x, int y, int durationMs = 650)
    {
        MoveTo(x, y, durationMs);
        Thread.Sleep(150);
        Click();
    }

    // ───────────────────────────── SendInput ─────────────────────────────

    private static INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 0x0002u : 0u } },
    };

    private static INPUT UnicodeInput(char c, bool up) => new()
    {
        type = 1,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004u | (up ? 0x0002u : 0u) } },
    };

    private static INPUT MouseInput(uint flags) => new() { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } };

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
}
