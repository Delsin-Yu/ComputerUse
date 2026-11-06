using System.Runtime.InteropServices;

namespace ComputerUse.Geo;

/// <summary>
/// Makes the process per-monitor DPI aware, and proves it. Every coordinate the tool
/// emits or consumes is a physical pixel; a process that silently runs DPI-unaware would report a
/// 3840x2160 panel as 2560x1440 and invent a 1280 px gap next to it, so failing loudly is the only
/// honest option.
/// </summary>
internal static class Dpi
{
    /// <summary>The per-monitor awareness context handle Windows accepts in SetProcessDpiAwarenessContext.</summary>
    private const int PerMonitorAware = -4;

    public const int AwarenessUnaware = 0;
    public const int AwarenessSystem = 1;
    public const int AwarenessPerMonitor = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    private static extern nint GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint context);

    public static int AwarenessCode { get; private set; } = AwarenessUnaware;

    public static string Awareness { get; private set; } = "unknown";

    public static void Ensure()
    {
        _ = SetProcessDpiAwarenessContext((nint)PerMonitorAware);

        AwarenessCode = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
        Awareness = AwarenessCode switch
        {
            AwarenessUnaware => "unaware",
            AwarenessSystem => "system",
            // GetAwarenessFromDpiAwarenessContext reports the same value for both per-monitor
            // contexts, so the honest name for what was measured is "per-monitor".
            AwarenessPerMonitor => "per-monitor",
            _ => "unknown-" + AwarenessCode,
        };

        if (AwarenessCode != AwarenessPerMonitor)
        {
            throw new Cli.CliError(
                "DpiAwarenessRequired",
                "Could not enable per-monitor DPI awareness (current: " + Awareness + "). "
                + "Refusing to report or act on coordinates that would be inconsistent between monitors.",
                hints:
                [
                    "this process must run with per-monitor awareness; a DPI-unaware host reports scaled rectangles",
                ]);
        }
    }
}
