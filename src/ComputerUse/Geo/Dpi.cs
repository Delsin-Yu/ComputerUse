using System.Runtime.InteropServices;

namespace ComputerUse.Geo;

/// <summary>
/// Makes the process a per-monitor-v2 DPI aware process, and proves it. Every coordinate the tool
/// emits or consumes is a physical pixel; a process that silently runs DPI-unaware would report a
/// 3840x2160 panel as 2560x1440 and invent a 1280 px gap next to it, so failing loudly is the only
/// honest option.
/// </summary>
internal static class Dpi
{
    private const int PerMonitorAwareV2 = -4;

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
        _ = SetProcessDpiAwarenessContext((nint)PerMonitorAwareV2);

        AwarenessCode = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
        Awareness = AwarenessCode switch
        {
            AwarenessUnaware => "unaware",
            AwarenessSystem => "system",
            AwarenessPerMonitor => "per-monitor-v2",
            _ => "unknown-" + AwarenessCode,
        };

        if (AwarenessCode != AwarenessPerMonitor)
        {
            throw new Cli.CliError(
                "DpiAwarenessRequired",
                "Could not enable per-monitor-v2 DPI awareness (current: " + Awareness + "). "
                + "Refusing to report or act on coordinates that would be inconsistent between monitors.",
                hints:
                [
                    "this process must run with per-monitor-v2 awareness; a DPI-unaware host reports scaled rectangles",
                ]);
        }
    }
}
