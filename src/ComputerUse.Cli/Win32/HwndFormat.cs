namespace ComputerUse.Cli.Win32;

internal static class HwndFormat
{
    public static string ToString(nint hwnd) => $"0x{hwnd.ToInt64():X8}";

    public static nint Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException("InvalidArgument", "--hwnd must not be empty.");
        }

        var trimmed = value.Trim();
        try
        {
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return (nint)Convert.ToInt64(trimmed, 16);
            }

            return (nint)long.Parse(trimmed, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new CliException("InvalidArgument", $"Invalid hwnd '{value}'. Use decimal or 0x-hex.");
        }
    }
}
