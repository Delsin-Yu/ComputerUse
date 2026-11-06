using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Grab;

/// <summary>Raw GDI capture primitives shared by the window and monitor paths.</summary>
internal static class Gdi
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint RenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint src, int srcX, int srcY, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    /// <summary>Copies composed screen pixels. In a per-monitor aware process these are physical pixels.</summary>
    public static Bitmap FromScreen(RectI region)
    {
        if (region.W <= 0 || region.H <= 0)
        {
            throw new CliError("CaptureFailed", $"Capture region {region} is empty.");
        }

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new CliError("CaptureFailed", "GetDC(NULL) failed.");
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previous = nint.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, region.W, region.H);

            if (memoryDc == nint.Zero || bitmap == nint.Zero)
            {
                throw new CliError("CaptureFailed", "Could not create a compatible DC/bitmap.");
            }

            previous = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, region.W, region.H, screenDc, region.X, region.Y, SrcCopy))
            {
                throw new CliError("CaptureFailed", $"BitBlt failed for region {region}.");
            }

            var copy = BitmapFromHBitmap(bitmap);
            return copy;
        }
        finally
        {
            if (previous != nint.Zero)
            {
                _ = SelectObject(memoryDc, previous);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    /// <summary>Asks the window to render itself; works while occluded, fails on some GPU surfaces.</summary>
    public static Bitmap? TryPrintWindow(nint hwnd, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            return null;
        }

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previous = nint.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);

            if (memoryDc == nint.Zero || bitmap == nint.Zero)
            {
                return null;
            }

            previous = SelectObject(memoryDc, bitmap);

            if (!PrintWindow(hwnd, memoryDc, RenderFullContent))
            {
                return null;
            }

            return BitmapFromHBitmap(bitmap);
        }
        finally
        {
            if (previous != nint.Zero)
            {
                _ = SelectObject(memoryDc, previous);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static Bitmap BitmapFromHBitmap(nint hBitmap)
    {
        using var image = Image.FromHbitmap(hBitmap);
        return new Bitmap(image);
    }
}
