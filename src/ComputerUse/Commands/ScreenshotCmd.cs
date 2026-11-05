using System.Drawing;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Grab;
using ComputerUse.Input;

namespace ComputerUse.Commands;

internal static class ScreenshotCmd
{
    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        "--window", "--rect", "--at", "--size", "--zoom", "--grid-step",
        "--mark", "--format", "--quality", "--out", "--settle",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--cursor" };

    private static readonly HashSet<string> Optional = new(StringComparer.Ordinal) { "--monitor", "--grid" };

    public static int Run(string[] argv, int start)
    {
        Dispatch.LastCommand = "screenshot";
        var args = Args.Parse(argv, start, "screenshot", Usage.Screenshot, Values, Flags, Optional);

        var windowSelector = args.Get("--window");
        var hasMonitor = args.Has("--monitor");

        if (windowSelector is not null && hasMonitor)
        {
            throw new UsageException("Use either --window or --monitor, not both.", Usage.Screenshot);
        }

        if (windowSelector is null && !hasMonitor)
        {
            throw new UsageException("screenshot needs --window <sel> or --monitor.", Usage.Screenshot);
        }

        var zoom = args.IntOr("--zoom", 1);
        if (zoom is < 1 or > 8)
        {
            throw new UsageException("--zoom must be between 1 and 8.", Usage.Screenshot);
        }

        var settle = args.IntOr("--settle", 0);
        var format = (args.Get("--format") ?? "png").ToLowerInvariant();
        if (format is not ("png" or "jpg"))
        {
            throw new UsageException("--format must be png or jpg.", Usage.Screenshot);
        }

        var quality = args.IntOr("--quality", 90);

        Space space;
        Captured capture;
        var target = new System.Text.Json.Nodes.JsonObject();

        if (windowSelector is not null)
        {
            var window = WindowList.Resolve(windowSelector, args.Int("--index"));
            space = Space.ForWindow(window);
            capture = CaptureStable(() => WindowGrab.Capture(window), settle);
            target = Targets.WindowTarget(window);
        }
        else
        {
            var monitor = Targets.ResolveMonitor(args.Optional("--monitor"));
            space = Space.Desktop(monitor);
            capture = CaptureStable(() => MonitorGrab.Capture(monitor.Rect), settle);
            target = Targets.MonitorTarget(monitor);
        }

        using (capture.Image)
        {
            var response = new Response("screenshot");
            response.Root["target"] = target;
            response.Pointer(Pointer.Get());

            if (!capture.SizeMatchesSource)
            {
                response.Warn(
                    $"capture-size-mismatch: the window rendered {capture.Image.Width}x{capture.Image.Height} pixels "
                    + $"for a {capture.ScreenRect.W}x{capture.ScreenRect.H} point rectangle");
            }

            var extent = space.Extent;
            var asked = ResolveCrop(args, extent, zoom);
            var crop = ClipTo(asked, extent);

            if (crop != asked)
            {
                response.Warn("crop-clipped: the requested rectangle reached past the capture and was clipped");
            }

            var nativeOrigin = space.CaptureOrigin;
            var nx = (int)Math.Round((crop.X - nativeOrigin.X) * capture.NativePxPerPoint);
            var ny = (int)Math.Round((crop.Y - nativeOrigin.Y) * capture.NativePxPerPoint);
            var nw = (int)Math.Round(crop.W * capture.NativePxPerPoint);
            var nh = (int)Math.Round(crop.H * capture.NativePxPerPoint);

            if (nx < 0 || ny < 0 || nx >= capture.Image.Width || ny >= capture.Image.Height)
            {
                throw new CliError(
                    "CaptureRegionOutsideImage",
                    $"Crop {crop} does not overlap the captured image.",
                    context: new System.Text.Json.Nodes.JsonObject
                    {
                        ["crop"] = Out.Rect(crop),
                        ["capture"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["width"] = capture.Image.Width,
                            ["height"] = capture.Image.Height,
                        },
                    });
            }

            nw = Math.Clamp(nw, 1, capture.Image.Width - nx);
            nh = Math.Clamp(nh, 1, capture.Image.Height - ny);

            using var cropped = ImageFx.Crop(capture.Image, nx, ny, nw, nh);

            Bitmap? zoomed = null;
            Bitmap working = cropped;
            if (zoom > 1)
            {
                zoomed = ImageFx.Zoom(cropped, zoom);
                working = zoomed;
            }

            using var fitted = ImageFx.Fit(working, Outputs.MaxImageSide);
            zoomed?.Dispose();

            var pxPerPoint = fitted.Width / (double)crop.W;
            var spaceOrigin = new PointI(crop.X, crop.Y);

            if (args.Has("--grid"))
            {
                Overlay.Grid(fitted, spaceOrigin, pxPerPoint, GridStep(args, pxPerPoint));
            }

            foreach (var mark in args.All("--mark"))
            {
                var (point, label) = args.ParseMark(mark);
                Overlay.Mark(fitted, spaceOrigin, pxPerPoint, point, label, Color.DeepSkyBlue, true);
            }

            if (args.Flag("--cursor"))
            {
                var pointerSpace = space.ToSpace(Pointer.Get());
                if (crop.Contains(pointerSpace))
                {
                    Overlay.Mark(fitted, spaceOrigin, pxPerPoint, pointerSpace, "cursor", Color.OrangeRed, false);
                }
                else
                {
                    response.Warn("cursor-marker-skipped: the pointer is outside the captured region");
                }
            }

            var path = Outputs.Resolve(args.Get("--out"), "screenshot", "." + format);
            ImageFx.Save(fitted, path, format, quality);

            response.Root["image"] = Outputs.ImageJson(path, fitted.Width, fitted.Height);
            response.Root["geometry"] = Outputs.GeometryJson(space, crop, pxPerPoint, zoom, capture, fitted.Width, fitted.Height);

            if (pxPerPoint < 0.5)
            {
                response.Warn(
                    $"too-coarse-for-aiming: one image pixel covers {1 / pxPerPoint:0.##} coordinate points; "
                    + "capture a crop with --at/--size or --zoom before aiming at something small");
            }

            return response.Emit();
        }
    }

    private static RectI ResolveCrop(Args args, RectI extent, int zoom)
    {
        if (args.Rect("--rect") is { } rect)
        {
            return rect;
        }

        var at = args.Point("--at");
        var size = args.Int("--size");

        if (at is null && size is null)
        {
            return extent;
        }

        if (at is null)
        {
            throw new UsageException("--size needs --at.", Usage.Screenshot);
        }

        // Without --size a crop at --zoom fills the 1024 px budget exactly.
        size ??= Math.Max(64, (int)Math.Round(Outputs.MaxImageSide / (double)Math.Max(1, zoom)));

        if (size < 8)
        {
            throw new UsageException("--size must be at least 8.", Usage.Screenshot);
        }

        return new RectI(at.Value.X - (size.Value / 2), at.Value.Y - (size.Value / 2), size.Value, size.Value);
    }

    private static int GridStep(Args args, double pxPerPoint)
    {
        var explicitValue = args.Optional("--grid") ?? args.Get("--grid-step");
        if (explicitValue is not null)
        {
            if (!int.TryParse(explicitValue, out var step) || step < 5)
            {
                throw new UsageException("--grid step must be an integer of at least 5.", Usage.Screenshot);
            }

            return step;
        }

        foreach (var candidate in new[] { 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 })
        {
            if (candidate * pxPerPoint >= 60)
            {
                return candidate;
            }
        }

        return 2000;
    }

    private static RectI ClipTo(RectI rect, RectI bounds)
    {
        var left = Math.Max(rect.X, bounds.X);
        var top = Math.Max(rect.Y, bounds.Y);
        var right = Math.Min(rect.Right, bounds.Right);
        var bottom = Math.Min(rect.Bottom, bounds.Bottom);

        return new RectI(left, top, right - left, bottom - top);
    }

    /// <summary>Re-captures until two frames match, so animations do not end up in the picture.</summary>
    private static Captured CaptureStable(Func<Captured> capture, int settleMs)
    {
        var current = capture();
        if (settleMs <= 0)
        {
            return current;
        }

        var deadline = Environment.TickCount64 + settleMs;
        var fingerprint = Fingerprint(current.Image);

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(40);
            var next = capture();
            var nextFingerprint = Fingerprint(next.Image);

            if (nextFingerprint == fingerprint)
            {
                current.Image.Dispose();
                return next;
            }

            current.Image.Dispose();
            current = next;
            fingerprint = nextFingerprint;
        }

        return current;
    }

    private static long Fingerprint(Bitmap bitmap)
    {
        long hash = 17;
        var stepX = Math.Max(1, bitmap.Width / 16);
        var stepY = Math.Max(1, bitmap.Height / 16);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                hash = (hash * 31) + bitmap.GetPixel(x, y).ToArgb();
            }
        }

        return hash;
    }
}
