using ComputerUse.Geo;

namespace ComputerUse.Commands;

/// <summary>
/// The coordinate space a command works in. With a window target every number is a window pixel
/// (origin = the window frame's top-left, physical pixels, may be negative); otherwise numbers are
/// desktop physical pixels. One rule for every command, so a number read off a screenshot can be
/// typed straight back into an action.
/// </summary>
internal sealed class Space
{
    private Space(string kind, PointI origin, WindowInfo? window, MonitorInfo? monitor)
    {
        Kind = kind;
        Origin = origin;
        Window = window;
        Monitor = monitor;
    }

    public string Kind { get; }

    public PointI Origin { get; }

    public WindowInfo? Window { get; }

    public MonitorInfo? Monitor { get; }

    public bool IsWindow => Window is not null;

    public static Space ForWindow(WindowInfo window) =>
        new("window", new PointI(window.Rect.X, window.Rect.Y), window, null);

    public static Space Desktop(MonitorInfo? monitor) =>
        new("desktop", new PointI(0, 0), null, monitor);

    public PointI ToScreen(PointI point) => new(point.X + Origin.X, point.Y + Origin.Y);

    public PointI ToSpace(PointI point) => new(point.X - Origin.X, point.Y - Origin.Y);

    public RectI ToScreen(RectI rect) => new(rect.X + Origin.X, rect.Y + Origin.Y, rect.W, rect.H);

    /// <summary>What the space covers, expressed in space coordinates.</summary>
    public RectI Extent => Window is { } window
        ? new RectI(0, 0, window.Rect.W, window.Rect.H)
        : Monitor?.Rect ?? Monitors.VirtualBounds();

    /// <summary>Space coordinate of the captured image's top-left corner.</summary>
    public PointI CaptureOrigin => Window is not null ? new PointI(0, 0) : new PointI(Monitor!.Rect.X, Monitor!.Rect.Y);
}
