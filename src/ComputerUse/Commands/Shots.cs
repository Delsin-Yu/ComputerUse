using System.Drawing;
using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Grab;

namespace ComputerUse.Commands;

/// <summary>
/// The confirmation picture an action can return: a small zoomed crop around where the pointer was
/// asked to go, with both the requested and the landed position drawn on it.
/// </summary>
internal static class Shots
{
    private const int Size = 240;

    public static (JsonObject Image, JsonObject Geometry) Around(
        PointI centerScreen,
        PointI? requestedScreen,
        PointI? landedScreen,
        int zoom,
        string kind)
    {
        var monitor = Monitors.At(centerScreen) ?? Monitors.Nearest(centerScreen);
        var region = Clip(new RectI(centerScreen.X - (Size / 2), centerScreen.Y - (Size / 2), Size, Size), monitor.Rect);

        using var raw = Gdi.FromScreen(region);
        using var zoomed = ImageFx.Zoom(raw, Math.Clamp(zoom, 1, 8));

        var pxPerPoint = zoomed.Width / (double)region.W;
        var origin = new PointI(region.X, region.Y);

        if (requestedScreen is { } requested && landedScreen is { } landed && requested != landed)
        {
            Overlay.Mark(zoomed, origin, pxPerPoint, requested, "requested", Color.DeepSkyBlue, true);
            Overlay.Mark(zoomed, origin, pxPerPoint, landed, "landed", Color.OrangeRed, true);
        }
        else if (landedScreen is { } single)
        {
            Overlay.Mark(zoomed, origin, pxPerPoint, single, "landed = requested", Color.OrangeRed, true);
        }

        var path = Outputs.Resolve(null, kind, ".png");
        ImageFx.Save(zoomed, path, "png", 90);

        var geometry = new JsonObject
        {
            ["space"] = "desktop",
            ["origin"] = Out.Point(origin),
            ["pxPerPoint"] = Math.Round(pxPerPoint, 4),
            ["crop"] = Out.Rect(region),
            ["map"] = "screenX = origin.x + imageX / pxPerPoint",
        };

        return (Outputs.ImageJson(path, zoomed.Width, zoomed.Height), geometry);
    }

    private static RectI Clip(RectI rect, RectI bounds)
    {
        var left = Math.Max(rect.X, bounds.X);
        var top = Math.Max(rect.Y, bounds.Y);
        var right = Math.Min(rect.Right, bounds.Right);
        var bottom = Math.Min(rect.Bottom, bounds.Bottom);

        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);
        return new RectI(left, top, width, height);
    }
}
