using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Grab;

namespace ComputerUse.Commands;

internal static class Outputs
{
    public const int MaxImageSide = 1024;

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmssfff");

    /// <summary>
    /// Images land in %TEMP%\computer-use unless the caller names a path. A directory (or a path
    /// ending with a separator) gets a generated name.
    /// </summary>
    public static string Resolve(string? argument, string kind, string extension)
    {
        var fallback = Path.Combine(Path.GetTempPath(), "computer-use", kind + "-" + Stamp() + extension);

        if (string.IsNullOrWhiteSpace(argument))
        {
            return fallback;
        }

        var trimmed = argument.Trim().Trim('"');
        var endsWithSeparator = trimmed.EndsWith(Path.DirectorySeparatorChar)
                                || trimmed.EndsWith(Path.AltDirectorySeparatorChar);

        if (endsWithSeparator || Directory.Exists(trimmed))
        {
            return Path.Combine(Path.GetFullPath(trimmed), kind + "-" + Stamp() + extension);
        }

        return Path.GetFullPath(trimmed);
    }

    public static JsonObject ImageJson(string path, int width, int height) => new()
    {
        ["path"] = path,
        ["format"] = Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),
        ["width"] = width,
        ["height"] = height,
        ["bytes"] = File.Exists(path) ? new FileInfo(path).Length : 0,
    };

    public static JsonObject GeometryJson(
        Space space,
        RectI crop,
        double pxPerPoint,
        int zoom,
        Captured capture,
        int imageWidth,
        int imageHeight) => new()
    {
        ["space"] = space.Kind,
        ["origin"] = Out.Point(space.ToScreen(new PointI(crop.X, crop.Y))),
        ["pxPerPoint"] = Math.Round(pxPerPoint, 4),
        ["zoom"] = zoom,
        ["capture"] = new JsonObject
        {
            ["width"] = capture.Image.Width,
            ["height"] = capture.Image.Height,
            ["source"] = capture.Source,
        },
        ["crop"] = Out.Rect(crop),
        ["width"] = imageWidth,
        ["height"] = imageHeight,
        ["map"] = "screenX = origin.x + imageX / pxPerPoint",
    };
}
