using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Grab;

internal static class MonitorGrab
{
    /// <summary>Composed pixels of one monitor: what a person looking at that screen sees.</summary>
    public static Captured Capture(RectI monitorRect)
    {
        var image = Gdi.FromScreen(monitorRect);
        return new Captured(image, monitorRect, 1.0, "monitor");
    }
}
