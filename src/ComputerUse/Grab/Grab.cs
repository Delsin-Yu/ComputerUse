using System.Drawing;
using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Grab;

/// <summary>
/// One capture: the pixels, the desktop rectangle they came from, and how many image pixels
/// one coordinate point occupies (not always 1 — a DPI-unaware window hands back a scaled bitmap).
/// </summary>
internal sealed record Captured(Bitmap Image, RectI ScreenRect, double NativePxPerPoint, string Source) : IDisposable
{
    public bool SizeMatchesSource => Math.Abs(NativePxPerPoint - 1.0) < 0.001;

    public void Dispose() => Image.Dispose();

    public static void EnsureNotBlank(Bitmap bitmap, string what, string[] hints)
    {
        var inspect = ImageFx.Inspect(bitmap);
        if (!inspect.Blank)
        {
            return;
        }

        throw new CliError(
            "CaptureBlank",
            $"{what} produced a blank image ({inspect.SampledColors} sampled colour(s)); refusing to report it as a picture of the UI.",
            hints: hints);
    }
}
