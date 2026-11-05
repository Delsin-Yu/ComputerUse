using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Input;

namespace ComputerUse.Commands;

internal static class Targets
{
    /// <summary>
    /// Resolves the acting space. With <c>--window</c> the window is brought to the foreground and
    /// re-read afterwards, because restoring a minimized window changes its rectangle.
    /// </summary>
    public static Space ForInput(Args args)
    {
        var selector = args.Get("--window");
        if (selector is null)
        {
            return Space.Desktop(null);
        }

        var window = WindowList.Resolve(selector, args.Int("--index"));
        var focused = WindowList.Focus(window.Handle);
        return Space.ForWindow(focused);
    }

    /// <summary>Resolves and focuses a window target for keyboard input; null when none was given.</summary>
    public static WindowInfo? FocusWindow(Args args)
    {
        var selector = args.Get("--window");
        if (selector is null)
        {
            return null;
        }

        var window = WindowList.Resolve(selector, args.Int("--index"));
        return WindowList.Focus(window.Handle);
    }

    /// <summary>Monitor for <c>--monitor</c>: explicit index or device, otherwise the one under the cursor.</summary>
    public static MonitorInfo ResolveMonitor(string? value)
    {
        if (value is null)
        {
            var pointer = Pointer.Get();
            return Monitors.At(pointer) ?? Monitors.Nearest(pointer);
        }

        if (int.TryParse(value, out var index))
        {
            return Monitors.ByIndex(index)
                   ?? throw new CliError(
                       "InvalidArgument",
                       $"No monitor with index {index}.",
                       1,
                       hints: ["run 'screen info' to list monitor indexes"]);
        }

        return Monitors.ByDevice(value)
               ?? throw new CliError(
                   "InvalidArgument",
                   $"No monitor named '{value}'.",
                   1,
                   hints: ["run 'screen info' to list monitor devices"]);
    }

    public static int Tolerance(Args args)
    {
        var tolerance = args.IntOr("--tolerance", 2);
        if (tolerance < 0 || tolerance > 100)
        {
            throw new UsageException("--tolerance must be between 0 and 100 pixels.", args.Help);
        }

        return tolerance;
    }

    public static JsonObject WindowTarget(WindowInfo window) => new()
    {
        ["kind"] = "window",
        ["space"] = "window",
        ["hwnd"] = window.Hwnd,
        ["title"] = window.Title,
        ["class"] = window.Class,
        ["process"] = window.Process,
        ["rect"] = Out.Rect(window.Rect),
        ["monitor"] = Monitors.At(window.Rect.Center)?.Index,
        ["foreground"] = window.Foreground,
        ["minimized"] = window.Minimized,
    };

    public static JsonObject MonitorTarget(MonitorInfo monitor) => new()
    {
        ["kind"] = "monitor",
        ["space"] = "desktop",
        ["index"] = monitor.Index,
        ["device"] = monitor.Device,
        ["rect"] = Out.Rect(monitor.Rect),
        ["dpi"] = monitor.Dpi,
        ["scale"] = monitor.ScalePercent,
    };

    /// <summary>The effect report every action returns.</summary>
    public static JsonObject InputReport(
        MoveReport report,
        Space space,
        string? strategyOverride = null,
        JsonObject? extra = null)
    {
        // The pointer report is in desktop pixels; the command speaks in its own space.
        var block = new JsonObject
        {
            ["requested"] = Out.Point(space.ToSpace(report.Requested)),
            ["landed"] = Out.Point(space.ToSpace(report.Landed)),
            ["landedScreen"] = Out.Point(report.Landed),
            ["delta"] = report.Delta,
            ["attempts"] = report.Attempts,
            ["strategy"] = strategyOverride ?? report.Strategy,
            ["pointerAfter"] = Out.Point(Pointer.Get()),
        };

        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                block[pair.Key] = pair.Value?.DeepClone();
            }
        }

        return block;
    }
}
