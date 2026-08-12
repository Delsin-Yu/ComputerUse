using System.Drawing;
using System.Drawing.Imaging;
using ComputerUse.Cli.Json;

namespace ComputerUse.Cli.Win32;

internal static class ScreenshotService
{
    public static ScreenshotDto Capture(string pathArgument, int? x, int? y, int? width, int? height, int? monitor)
    {
        var region = ScreenService.ResolveCaptureRegion(x, y, width, height, monitor);
        return CaptureRegionToPath(region, pathArgument);
    }

    public static ScreenshotDto CaptureRegionToPath(RectDto region, string pathArgument)
    {
        region = ScreenService.ClampToVirtual(region);
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new CliException("InvalidArgument", "Capture region width and height must be positive.");
        }

        var outputPath = ResolveOutputPath(pathArgument);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = CaptureRegionBitmap(region);
        bitmap.Save(outputPath, ImageFormat.Png);

        return new ScreenshotDto
        {
            Path = outputPath,
            FileName = Path.GetFileName(outputPath),
            Width = region.Width,
            Height = region.Height,
            Region = region,
        };
    }

    internal static Bitmap CaptureRegionBitmap(RectDto region) => CaptureRegion(region);

    internal static string ResolveOutputPath(string pathArgument)
    {
        if (string.IsNullOrWhiteSpace(pathArgument))
        {
            throw new CliException("InvalidArgument", "--path is required.");
        }

        var trimmed = pathArgument.Trim().Trim('"');
        var endsWithSeparator = trimmed.EndsWith(Path.DirectorySeparatorChar)
            || trimmed.EndsWith(Path.AltDirectorySeparatorChar);

        if (endsWithSeparator || Directory.Exists(trimmed))
        {
            var fileName = $"screenshot-{DateTime.Now:yyyyMMddHHmmssfff}.png";
            return Path.Combine(Path.GetFullPath(trimmed), fileName);
        }

        return Path.GetFullPath(trimmed);
    }

    private static Bitmap CaptureRegion(RectDto region)
    {
        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new CliException("Win32Error", "GetDC failed.", exitCode: 2);
        }

        nint memDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint old = nint.Zero;

        try
        {
            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, region.Width, region.Height);
            if (memDc == nint.Zero || bitmap == nint.Zero)
            {
                throw new CliException("Win32Error", "Failed to create compatible DC/bitmap.", exitCode: 2);
            }

            old = NativeMethods.SelectObject(memDc, bitmap);
            if (!NativeMethods.BitBlt(memDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, NativeMethods.SRCCOPY))
            {
                throw new CliException("Win32Error", "BitBlt failed.", exitCode: 2);
            }

            return Image.FromHbitmap(bitmap);
        }
        finally
        {
            if (old != nint.Zero)
            {
                NativeMethods.SelectObject(memDc, old);
            }

            if (bitmap != nint.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memDc != nint.Zero)
            {
                NativeMethods.DeleteDC(memDc);
            }

            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }
}
