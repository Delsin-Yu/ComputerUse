namespace ComputerUse.Cli.Win32;

internal static class KeyMap
{
    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backspace"] = 0x08,
        ["bs"] = 0x08,
        ["tab"] = 0x09,
        ["enter"] = 0x0D,
        ["return"] = 0x0D,
        ["shift"] = 0x10,
        ["ctrl"] = 0x11,
        ["control"] = 0x11,
        ["alt"] = 0x12,
        ["menu"] = 0x12,
        ["pause"] = 0x13,
        ["capslock"] = 0x14,
        ["caps"] = 0x14,
        ["escape"] = 0x1B,
        ["esc"] = 0x1B,
        ["space"] = 0x20,
        ["pageup"] = 0x21,
        ["pgup"] = 0x21,
        ["pagedown"] = 0x22,
        ["pgdn"] = 0x22,
        ["end"] = 0x23,
        ["home"] = 0x24,
        ["left"] = 0x25,
        ["up"] = 0x26,
        ["right"] = 0x27,
        ["down"] = 0x28,
        ["printscreen"] = 0x2C,
        ["prtsc"] = 0x2C,
        ["insert"] = 0x2D,
        ["ins"] = 0x2D,
        ["delete"] = 0x2E,
        ["del"] = 0x2E,
        ["win"] = 0x5B,
        ["lwin"] = 0x5B,
        ["rwin"] = 0x5C,
        ["apps"] = 0x5D,
        ["context"] = 0x5D,
        ["numpad0"] = 0x60,
        ["numpad1"] = 0x61,
        ["numpad2"] = 0x62,
        ["numpad3"] = 0x63,
        ["numpad4"] = 0x64,
        ["numpad5"] = 0x65,
        ["numpad6"] = 0x66,
        ["numpad7"] = 0x67,
        ["numpad8"] = 0x68,
        ["numpad9"] = 0x69,
        ["multiply"] = 0x6A,
        ["add"] = 0x6B,
        ["subtract"] = 0x6D,
        ["decimal"] = 0x6E,
        ["divide"] = 0x6F,
        ["f1"] = 0x70,
        ["f2"] = 0x71,
        ["f3"] = 0x72,
        ["f4"] = 0x73,
        ["f5"] = 0x74,
        ["f6"] = 0x75,
        ["f7"] = 0x76,
        ["f8"] = 0x77,
        ["f9"] = 0x78,
        ["f10"] = 0x79,
        ["f11"] = 0x7A,
        ["f12"] = 0x7B,
        ["f13"] = 0x7C,
        ["f14"] = 0x7D,
        ["f15"] = 0x7E,
        ["f16"] = 0x7F,
        ["f17"] = 0x80,
        ["f18"] = 0x81,
        ["f19"] = 0x82,
        ["f20"] = 0x83,
        ["f21"] = 0x84,
        ["f22"] = 0x85,
        ["f23"] = 0x86,
        ["f24"] = 0x87,
        ["numlock"] = 0x90,
        ["scrolllock"] = 0x91,
        ["lshift"] = 0xA0,
        ["rshift"] = 0xA1,
        ["lctrl"] = 0xA2,
        ["rctrl"] = 0xA3,
        ["lalt"] = 0xA4,
        ["ralt"] = 0xA5,
        [";"] = 0xBA,
        ["="] = 0xBB,
        [","] = 0xBC,
        ["-"] = 0xBD,
        ["."] = 0xBE,
        ["/"] = 0xBF,
        ["`"] = 0xC0,
        ["["] = 0xDB,
        ["\\"] = 0xDC,
        ["]"] = 0xDD,
        ["'"] = 0xDE,
    };

    private static readonly HashSet<ushort> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E,
        0x5B, 0x5C, 0x5D, 0x6F, 0x90, 0xA3, 0xA5,
    ];

    public static ushort Parse(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new CliException("InvalidArgument", "Key name must not be empty.");
        }

        var trimmed = key.Trim();

        if (Names.TryGetValue(trimmed, out var vk))
        {
            return vk;
        }

        if (trimmed.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
        {
            var hex = trimmed[3..];
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex[2..];
            }

            if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var fromHex))
            {
                return fromHex;
            }

            if (ushort.TryParse(hex, out var fromDec))
            {
                return fromDec;
            }
        }

        if (trimmed.Length == 1)
        {
            var ch = char.ToUpperInvariant(trimmed[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return ch;
            }
        }

        throw new CliException("InvalidArgument", $"Unknown key '{key}'.");
    }

    public static IReadOnlyList<ushort> ParseModifiers(string? modifiers)
    {
        if (string.IsNullOrWhiteSpace(modifiers))
        {
            return [];
        }

        return modifiers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Parse)
            .ToArray();
    }

    public static bool IsExtended(ushort vk) => ExtendedKeys.Contains(vk);
}
