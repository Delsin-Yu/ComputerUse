using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Grab;

internal static class WindowGrab
{
    /// <summary>
    /// The window's own pixels, without touching the foreground window. PrintWindow is tried first
    /// (works while covered); if it hands back a blank surface — GPU-composed content, a minimized
    /// window — the screen pixels are used only when the window is actually visible, otherwise the
    /// call fails instead of returning a lie.
    /// </summary>
    public static Captured Capture(WindowInfo window)
    {
        var rect = window.Rect;
        if (rect.IsEmpty)
        {
            throw new CliError("CaptureFailed", $"Window {window.Hwnd} has an empty rectangle.");
        }

        var printed = Gdi.TryPrintWindow(window.Handle, rect.W, rect.H);
        if (printed is not null)
        {
            if (!ImageFx.Inspect(printed).Blank)
            {
                var pxPerPoint = printed.Width / (double)rect.W;
                return new Captured(printed, rect, pxPerPoint, "printwindow");
            }

            printed.Dispose();
        }

        if (window.Visible && !window.Minimized)
        {
            var screenCopy = Gdi.FromScreen(rect);
            Captured.EnsureNotBlank(screenCopy, $"Screen copy of window {window.Hwnd}", BuildHints(window));
            return new Captured(screenCopy, rect, 1.0, "screen");
        }

        throw new CliError(
            "CaptureBlank",
            $"Window {window.Hwnd} did not render into an offscreen bitmap and is not on screen, so there is nothing honest to capture.",
            context: new System.Text.Json.Nodes.JsonObject
            {
                ["hwnd"] = window.Hwnd,
                ["visible"] = window.Visible,
                ["minimized"] = window.Minimized,
            },
            hints: BuildHints(window));
    }

    private static string[] BuildHints(WindowInfo window)
    {
        var hints = new List<string>
        {
            "the window may render through a GPU surface that PrintWindow cannot read",
        };

        if (window.Minimized)
        {
            hints.Add("the window is minimized: run 'window move-to-foreground' first, then capture it");
        }

        hints.Add("if the window is visible, 'screenshot --monitor' shows the composed pixels instead");
        return hints.ToArray();
    }
}
