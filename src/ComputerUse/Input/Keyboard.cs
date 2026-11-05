using System.Runtime.InteropServices;
using ComputerUse.Cli;

namespace ComputerUse.Input;

/// <summary>
/// Every keystroke this tool sends leaves through SendInput, and the count that Windows reports
/// back is checked: a call that only partly landed raises instead of pretending the keys went out.
/// Nothing here retries or falls back to keybd_event, so a failure means "no keys, or not all".
/// </summary>
internal static class Keyboard
{
    private const uint InputKeyboard = 1;

    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventScancode = 0x0008;

    private const uint MapVkToScancode = 0;

    /// <summary>SendInput's documented limit is 64 events per call; stay well inside it.</summary>
    private const int MaxEventsPerCall = 64;

    private const ushort VkTab = 0x09;
    private const ushort VkEnter = 0x0D;
    private const ushort VkShiftLeft = 0xA0;

    /// <summary>Combo tokens that may precede the key, mapped to their virtual-key codes.</summary>
    private static readonly Dictionary<string, ushort> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xA2,
        ["control"] = 0xA2,
        ["lctrl"] = 0xA2,
        ["rctrl"] = 0xA3,
        ["shift"] = 0xA0,
        ["lshift"] = 0xA0,
        ["rshift"] = 0xA1,
        ["alt"] = 0xA4,
        ["lalt"] = 0xA4,
        ["ralt"] = 0xA5,
        ["win"] = 0x5B,
        ["lwin"] = 0x5B,
        ["rwin"] = 0x5C,
    };

    /// <summary>
    /// Types <paramref name="text"/> as literal characters.
    /// <paramref name="method"/> is <c>unicode</c> (default; KEYEVENTF_UNICODE, one event pair per
    /// UTF-16 code unit, so surrogate pairs pair up in Windows) or <c>scan</c>
    /// (KEYEVENTF_SCANCODE via MapVirtualKey, ASCII only, Shift held for uppercase and shifted
    /// punctuation). <c>'\n'</c> is an Enter tap, <c>'\r'</c> is skipped, <c>'\t'</c> a Tab tap.
    /// </summary>
    /// <param name="delayMs">Sleep between characters; 0 batches them into one SendInput burst.</param>
    /// <exception cref="CliError">Unknown method, or scan mode met a character with no scan code.</exception>
    public static void TypeText(string text, int delayMs, string method)
    {
        var mode = (method ?? string.Empty).Trim();
        if (mode.Length == 0)
        {
            mode = "unicode";
        }

        if (string.Equals(mode, "unicode", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(text))
            {
                TypeUnicode(text, delayMs);
            }

            return;
        }

        if (string.Equals(mode, "scan", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(text))
            {
                TypeScan(text, delayMs);
            }

            return;
        }

        throw new CliError(
            "InvalidArgument",
            $"Unknown --method '{method}' (expected unicode or scan).",
            1,
            hints: ["--method unicode sends literal characters; --method scan sends hardware scan codes"]);
    }

    /// <summary>
    /// Taps a key combo such as <c>ctrl+shift+t</c>, <c>enter</c> or <c>f5</c>: modifiers down in
    /// order, the key tapped once, modifiers released in reverse. The whole sequence is repeated
    /// <paramref name="repeat"/> times with <paramref name="delayMs"/> between repeats.
    /// </summary>
    /// <exception cref="CliError">Malformed combo, unknown key, unknown modifier, or repeat out of range.</exception>
    public static void Press(string combo, int repeat, int delayMs, bool scanCode = false)
    {
        if (repeat < 1 || repeat > 50)
        {
            throw new CliError(
                "InvalidArgument",
                $"Repeat count must be between 1 and 50 (got {repeat}).",
                1,
                hints: ["use several presses rather than one huge repeat count"]);
        }

        var modifiers = new List<ushort>(3);
        var key = default(ushort);

        var tokens = (combo ?? string.Empty).Split('+', StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length == 0)
            {
                throw new CliError(
                    "InvalidArgument",
                    $"Empty token in key combo '{combo}'.",
                    1,
                    hints: ["spell combos as ctrl+shift+t: exactly one key, preceded by modifiers"]);
            }

            if (i == tokens.Length - 1)
            {
                key = KeyMap.ToVk(token);
                continue;
            }

            if (!Modifiers.TryGetValue(token, out var modifier))
            {
                throw new CliError(
                    "InvalidArgument",
                    $"'{token}' is not a modifier; only the last token of a combo is the key.",
                    1,
                    hints: ["modifiers are ctrl, shift, alt and win, each with left/right forms (lctrl, rshift, ralt)"]);
            }

            modifiers.Add(modifier);
        }

        var events = new List<InputEvent>(modifiers.Count * 2 + 2);
        foreach (var modifier in modifiers)
        {
            events.Add(KeyEvent(modifier, up: false, scanCode));
        }

        events.Add(KeyEvent(key, up: false, scanCode));
        events.Add(KeyEvent(key, up: true, scanCode));

        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            events.Add(KeyEvent(modifiers[i], up: true, scanCode));
        }

        var sequence = events.ToArray();

        for (var i = 0; i < repeat; i++)
        {
            if (i > 0)
            {
                Wait(delayMs);
            }

            Send(sequence);
        }
    }

    /// <summary>Presses one key down and leaves it held.</summary>
    /// <exception cref="CliError">Unknown key name, or SendInput refused the event.</exception>
    public static void Down(string keyName, bool scanCode = false)
    {
        var vk = KeyMap.ToVk(keyName);
        Send([KeyEvent(vk, up: false, scanCode)]);
    }

    /// <summary>Releases one held key.</summary>
    /// <exception cref="CliError">Unknown key name, or SendInput refused the event.</exception>
    public static void Up(string keyName, bool scanCode = false)
    {
        var vk = KeyMap.ToVk(keyName);
        Send([KeyEvent(vk, up: true, scanCode)]);
    }

    /// <summary>True when <paramref name="text"/> is a combo such as <c>ctrl+t</c>, not one key.</summary>
    public static bool IsCombo(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('+'))
        {
            return false;
        }

        return text.Split('+', StringSplitOptions.TrimEntries).Length > 1;
    }

    private static void TypeUnicode(string text, int delayMs)
    {
        var batch = new List<InputEvent>(32);

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\r':
                    continue;
                case '\n':
                    AddTap(batch, VkEnter);
                    break;
                case '\t':
                    AddTap(batch, VkTab);
                    break;
                default:
                    AddUnicode(batch, ch);
                    break;
            }

            if (delayMs > 0)
            {
                Flush(batch);
                Wait(delayMs);
            }
        }

        Send(batch.ToArray());
    }

    private static void TypeScan(string text, int delayMs)
    {
        // Resolve every character before sending anything: a text that cannot be typed in full
        // must fail without leaving half of it behind.
        var strokes = new List<(ushort Scan, bool Shift)>(text.Length);
        foreach (var ch in text)
        {
            if (ch == '\r')
            {
                continue;
            }

            var scan = TryMapChar(ch, out var vk, out var shift) ? ScanCodeOf(vk) : (ushort)0;
            if (scan == 0)
            {
                throw ScanFailure();
            }

            strokes.Add((scan, shift));
        }

        var shiftScan = ScanCodeOf(VkShiftLeft);
        var batch = new List<InputEvent>(32);

        foreach (var (scan, shift) in strokes)
        {
            if (shift)
            {
                batch.Add(ScanEvent(shiftScan, up: false));
            }

            batch.Add(ScanEvent(scan, up: false));
            batch.Add(ScanEvent(scan, up: true));

            if (shift)
            {
                batch.Add(ScanEvent(shiftScan, up: true));
            }

            if (delayMs > 0)
            {
                Flush(batch);
                Wait(delayMs);
            }
        }

        Send(batch.ToArray());
    }

    /// <summary>
    /// Maps one ASCII character to the key that produces it and says whether Shift is needed.
    /// Mirrors the ASCII subset of <see cref="KeyMap"/>; anything else is not typeable by scan code.
    /// </summary>
    private static bool TryMapChar(char ch, out ushort vk, out bool shift)
    {
        shift = false;

        switch (ch)
        {
            case >= 'a' and <= 'z':
                vk = (ushort)(0x41 + (ch - 'a'));
                return true;
            case >= 'A' and <= 'Z':
                vk = (ushort)(0x41 + (ch - 'A'));
                shift = true;
                return true;
            case >= '0' and <= '9':
                vk = (ushort)(0x30 + (ch - '0'));
                return true;
            case ' ':
                vk = 0x20;
                return true;
            case '\n':
                vk = VkEnter;
                return true;
            case '\t':
                vk = VkTab;
                return true;
            case '`':
                vk = 0xC0;
                return true;
            case '~':
                vk = 0xC0;
                shift = true;
                return true;
            case '-':
                vk = 0xBD;
                return true;
            case '_':
                vk = 0xBD;
                shift = true;
                return true;
            case '=':
                vk = 0xBB;
                return true;
            case '+':
                vk = 0xBB;
                shift = true;
                return true;
            case '[':
                vk = 0xDB;
                return true;
            case '{':
                vk = 0xDB;
                shift = true;
                return true;
            case ']':
                vk = 0xDD;
                return true;
            case '}':
                vk = 0xDD;
                shift = true;
                return true;
            case '\\':
                vk = 0xDC;
                return true;
            case '|':
                vk = 0xDC;
                shift = true;
                return true;
            case ';':
                vk = 0xBA;
                return true;
            case ':':
                vk = 0xBA;
                shift = true;
                return true;
            case '\'':
                vk = 0xDE;
                return true;
            case '"':
                vk = 0xDE;
                shift = true;
                return true;
            case ',':
                vk = 0xBC;
                return true;
            case '<':
                vk = 0xBC;
                shift = true;
                return true;
            case '.':
                vk = 0xBE;
                return true;
            case '>':
                vk = 0xBE;
                shift = true;
                return true;
            case '/':
                vk = 0xBF;
                return true;
            case '?':
                vk = 0xBF;
                shift = true;
                return true;
            case '!':
                vk = 0x31;
                shift = true;
                return true;
            case '@':
                vk = 0x32;
                shift = true;
                return true;
            case '#':
                vk = 0x33;
                shift = true;
                return true;
            case '$':
                vk = 0x34;
                shift = true;
                return true;
            case '%':
                vk = 0x35;
                shift = true;
                return true;
            case '^':
                vk = 0x36;
                shift = true;
                return true;
            case '&':
                vk = 0x37;
                shift = true;
                return true;
            case '*':
                vk = 0x38;
                shift = true;
                return true;
            case '(':
                vk = 0x39;
                shift = true;
                return true;
            case ')':
                vk = 0x30;
                shift = true;
                return true;
            default:
                vk = 0;
                return false;
        }
    }

    private static CliError ScanFailure()
    {
        return new CliError(
            "InvalidArgument",
            "--method scan cannot type non-ASCII text.",
            1,
            hints: ["use --method unicode (the default) for anything outside printable ASCII"]);
    }

    private static void AddTap(List<InputEvent> batch, ushort vk)
    {
        batch.Add(KeyEvent(vk, up: false));
        batch.Add(KeyEvent(vk, up: true));
    }

    private static void AddUnicode(List<InputEvent> batch, char codeUnit)
    {
        batch.Add(UnicodeEvent(codeUnit, up: false));
        batch.Add(UnicodeEvent(codeUnit, up: true));
    }

    private static InputEvent KeyEvent(ushort vk, bool up, bool scanCode = false)
    {
        if (scanCode)
        {
            var code = ScanCodeOf(vk);
            if (code == 0)
            {
                throw new CliError(
                    "InvalidArgument",
                    $"'{KeyMap.DisplayName(vk)}' has no scan code, so it cannot be sent as a scan-code event.",
                    1,
                    hints: ["use the default (virtual-key) method for this key"]);
            }

            var scanFlags = KeyEventScancode | (up ? KeyEventKeyUp : 0u);
            if (KeyMap.IsExtended(vk))
            {
                scanFlags |= KeyEventExtendedKey;
            }

            return new InputEvent
            {
                Type = InputKeyboard,
                Union = new InputEventUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = code,
                        Flags = scanFlags,
                        Time = 0,
                        ExtraInfo = 0,
                    },
                },
            };
        }

        var flags = up ? KeyEventKeyUp : 0u;
        if (KeyMap.IsExtended(vk))
        {
            flags |= KeyEventExtendedKey;
        }

        return new InputEvent
        {
            Type = InputKeyboard,
            Union = new InputEventUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = vk,
                    ScanCode = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = 0,
                },
            },
        };
    }

    private static InputEvent UnicodeEvent(char codeUnit, bool up)
    {
        return new InputEvent
        {
            Type = InputKeyboard,
            Union = new InputEventUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = codeUnit,
                    Flags = KeyEventUnicode | (up ? KeyEventKeyUp : 0u),
                    Time = 0,
                    ExtraInfo = 0,
                },
            },
        };
    }

    private static InputEvent ScanEvent(ushort scan, bool up)
    {
        return new InputEvent
        {
            Type = InputKeyboard,
            Union = new InputEventUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = scan,
                    Flags = KeyEventScancode | (up ? KeyEventKeyUp : 0u),
                    Time = 0,
                    ExtraInfo = 0,
                },
            },
        };
    }

    private static ushort ScanCodeOf(ushort vk)
    {
        return (ushort)MapVirtualKey(vk, MapVkToScancode);
    }

    private static void Wait(int delayMs)
    {
        if (delayMs > 0)
        {
            Thread.Sleep(delayMs);
        }
    }

    private static void Flush(List<InputEvent> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        Send(batch.ToArray());
        batch.Clear();
    }

    /// <summary>
    /// Hands the events to SendInput in batches of at most 64 and checks the returned count.
    /// A short count is a hard failure: the remaining events were never retried.
    /// </summary>
    private static unsafe void Send(InputEvent[] events)
    {
        for (var offset = 0; offset < events.Length; offset += MaxEventsPerCall)
        {
            var count = Math.Min(MaxEventsPerCall, events.Length - offset);
            var chunk = count == events.Length ? events : events[offset..(offset + count)];

            var sent = SendInput((uint)count, chunk, sizeof(InputEvent));
            if (sent != (uint)count)
            {
                var error = Marshal.GetLastWin32Error();
                throw new CliError(
                    "InputNotAccepted",
                    $"SendInput accepted {sent}/{count} events (win32 {error}).",
                    2,
                    hints: ["another process may hold the input queue; nothing was retried"]);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, InputEvent[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>INPUT; only the keyboard arm of the union is ever produced.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public uint Type;
        public InputEventUnion Union;
    }

    /// <summary>Union of the Win32 input payloads, sized by the largest member (MOUSEINPUT).</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputEventUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    /// <summary>KEYBDINPUT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
