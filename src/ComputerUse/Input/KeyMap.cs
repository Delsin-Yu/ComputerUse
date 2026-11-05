using System.Globalization;
using ComputerUse.Cli;

namespace ComputerUse.Input;

/// <summary>
/// The key vocabulary of the keyboard commands: the spellings a caller may use, the virtual-key
/// code each one means, and the canonical spelling reported back in JSON and error messages.
/// Lookups are case-insensitive; every code is a plain Win32 virtual-key value.
/// </summary>
internal static class KeyMap
{
    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkEnter = 0x0D;
    private const ushort VkPause = 0x13;
    private const ushort VkCapsLock = 0x14;
    private const ushort VkEscape = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkPageUp = 0x21;
    private const ushort VkPageDown = 0x22;
    private const ushort VkEnd = 0x23;
    private const ushort VkHome = 0x24;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkPrintScreen = 0x2C;
    private const ushort VkInsert = 0x2D;
    private const ushort VkDelete = 0x2E;
    private const ushort VkDigit0 = 0x30;
    private const ushort VkLetterA = 0x41;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkApps = 0x5D;
    private const ushort VkNumpad0 = 0x60;
    private const ushort VkNumpadMultiply = 0x6A;
    private const ushort VkNumpadAdd = 0x6B;
    private const ushort VkNumpadSubtract = 0x6D;
    private const ushort VkNumpadDecimal = 0x6E;
    private const ushort VkNumpadDivide = 0x6F;
    private const ushort VkF1 = 0x70;
    private const ushort VkNumLock = 0x90;
    private const ushort VkScrollLock = 0x91;
    private const ushort VkLShift = 0xA0;
    private const ushort VkRShift = 0xA1;
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkLAlt = 0xA4;
    private const ushort VkRAlt = 0xA5;
    private const ushort VkSemicolon = 0xBA;
    private const ushort VkEquals = 0xBB;
    private const ushort VkComma = 0xBC;
    private const ushort VkMinus = 0xBD;
    private const ushort VkPeriod = 0xBE;
    private const ushort VkSlash = 0xBF;
    private const ushort VkBackquote = 0xC0;
    private const ushort VkBracketLeft = 0xDB;
    private const ushort VkBackslash = 0xDC;
    private const ushort VkBracketRight = 0xDD;
    private const ushort VkQuote = 0xDE;

    private const string RawPrefix = "vk:";

    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<ushort, string> Canonical = new();

    static KeyMap()
    {
        for (var c = 'a'; c <= 'z'; c++)
        {
            Add(c.ToString(), (ushort)(VkLetterA + (c - 'a')));
        }

        for (var d = '0'; d <= '9'; d++)
        {
            Add(d.ToString(), (ushort)(VkDigit0 + (d - '0')));
        }

        for (var i = 1; i <= 24; i++)
        {
            Add("f" + i.ToString(CultureInfo.InvariantCulture), (ushort)(VkF1 + (i - 1)));
        }

        for (var i = 0; i <= 9; i++)
        {
            Add("numpad" + i.ToString(CultureInfo.InvariantCulture), (ushort)(VkNumpad0 + i));
        }

        Add("enter", VkEnter);
        Add("return", VkEnter);
        Add("tab", VkTab);
        Add("escape", VkEscape);
        Add("esc", VkEscape);
        Add("backspace", VkBack);
        Add("bs", VkBack);
        Add("delete", VkDelete);
        Add("del", VkDelete);
        Add("insert", VkInsert);
        Add("ins", VkInsert);
        Add("home", VkHome);
        Add("end", VkEnd);
        Add("pageup", VkPageUp);
        Add("pgup", VkPageUp);
        Add("pagedown", VkPageDown);
        Add("pgdn", VkPageDown);
        Add("up", VkUp);
        Add("arrowup", VkUp);
        Add("down", VkDown);
        Add("arrowdown", VkDown);
        Add("left", VkLeft);
        Add("arrowleft", VkLeft);
        Add("right", VkRight);
        Add("arrowright", VkRight);
        Add("space", VkSpace);
        Add("spacebar", VkSpace);
        Add("minus", VkMinus);
        Add("dash", VkMinus);
        Add("equals", VkEquals);
        Add("equal", VkEquals);
        Add("comma", VkComma);
        Add("period", VkPeriod);
        Add("dot", VkPeriod);
        Add("slash", VkSlash);
        Add("backslash", VkBackslash);
        Add("semicolon", VkSemicolon);
        Add("quote", VkQuote);
        Add("apostrophe", VkQuote);
        Add("backquote", VkBackquote);
        Add("tick", VkBackquote);
        Add("grave", VkBackquote);
        Add("bracketleft", VkBracketLeft);
        Add("lbracket", VkBracketLeft);
        Add("bracketright", VkBracketRight);
        Add("rbracket", VkBracketRight);
        Add("capslock", VkCapsLock);
        Add("numlock", VkNumLock);
        Add("scrolllock", VkScrollLock);
        Add("printscreen", VkPrintScreen);
        Add("prtsc", VkPrintScreen);
        Add("pause", VkPause);
        Add("apps", VkApps);
        Add("menu", VkApps);
        Add("win", VkLWin);
        Add("lwin", VkLWin);
        Add("rwin", VkRWin);
        Add("ctrl", VkLControl);
        Add("control", VkLControl);
        Add("lctrl", VkLControl);
        Add("rctrl", VkRControl);
        Add("shift", VkLShift);
        Add("lshift", VkLShift);
        Add("rshift", VkRShift);
        Add("alt", VkLAlt);
        Add("lalt", VkLAlt);
        Add("ralt", VkRAlt);
        Add("numpadmultiply", VkNumpadMultiply);
        Add("numpadadd", VkNumpadAdd);
        Add("numpadsubtract", VkNumpadSubtract);
        Add("numpaddecimal", VkNumpadDecimal);
        Add("numpaddivide", VkNumpadDivide);
    }

    /// <summary>
    /// Resolves a key name to its virtual-key code. Accepts any registered spelling and the
    /// raw form <c>vk:0x1B</c> (hex) or <c>vk:27</c> (decimal) for codes with no name.
    /// </summary>
    /// <exception cref="CliError">The name is not a known key.</exception>
    public static ushort ToVk(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var key = name.Trim();

            if (Names.TryGetValue(key, out var vk))
            {
                return vk;
            }

            if (key.Length > RawPrefix.Length && key.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var raw = key[RawPrefix.Length..];

                var isHex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                var digits = isHex ? raw[2..] : raw;
                var style = isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;

                if (ushort.TryParse(digits, style, CultureInfo.InvariantCulture, out var code)
                    && code >= 1
                    && code <= 0xFF)
                {
                    return code;
                }
            }
        }

        throw new CliError(
            "UnknownKey",
            $"Unknown key '{name}'.",
            1,
            hints:
            [
                "names: a-z, 0-9, f1-f24, enter, tab, escape, backspace, delete, insert, home, end, pageup, pagedown, "
                + "up, down, left, right, space, minus, equals, comma, period, slash, backslash, semicolon, quote, "
                + "backquote, bracketleft, bracketright, capslock, numlock, scrolllock, printscreen, pause, apps, "
                + "win, rwin, ctrl, rctrl, shift, rshift, alt, ralt, numpad0-numpad9, numpadmultiply, numpadadd, "
                + "numpadsubtract, numpaddecimal, numpaddivide",
                "any virtual-key code: vk:0x1B",
            ]);
    }

    /// <summary>
    /// True for keys Windows delivers with the extended-key flag: arrows, insert/delete,
    /// home/end, page up/down, num lock, print screen, the numpad divide, right ctrl/alt,
    /// and the win/apps keys.
    /// </summary>
    public static bool IsExtended(ushort vk)
    {
        switch (vk)
        {
            case VkInsert:
            case VkDelete:
            case VkHome:
            case VkEnd:
            case VkPageUp:
            case VkPageDown:
            case VkLeft:
            case VkUp:
            case VkRight:
            case VkDown:
            case VkPrintScreen:
            case VkNumpadDivide:
            case VkNumLock:
            case VkRControl:
            case VkRAlt:
            case VkLWin:
            case VkRWin:
            case VkApps:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Canonical lowercase spelling of a code, for error messages and JSON.</summary>
    public static string DisplayName(ushort vk)
    {
        return Canonical.TryGetValue(vk, out var name)
            ? name
            : RawPrefix + "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static void Add(string name, ushort vk)
    {
        Names[name] = vk;

        // First registration wins, so the canonical spelling is the plain name ("enter", not "return").
        Canonical.TryAdd(vk, name);
    }
}
