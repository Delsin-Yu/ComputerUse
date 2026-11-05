using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ComputerUse.Cli;

namespace ComputerUse.Grab;

/// <summary>
/// Cheap content probe over a coarse sample grid: whether a frame is blank, how many distinct
/// colours it carries and the luma range it spans. Used to tell an agent "this window is blank"
/// before it spends a look on the image.
/// </summary>
internal readonly record struct CaptureInspect(bool Blank, int SampledColors, double MinLuma, double MaxLuma);

/// <summary>
/// Bitmap crop / zoom / downscale / encode. Every method returns a caller-owned bitmap: the
/// source is never mutated, and the result must be disposed by the caller.
/// </summary>
internal static class ImageFx
{
    /// <summary>Samples per axis; 24x24 = 576 probes, cheap enough for a streaming grab loop.</summary>
    private const int SampleAxis = 24;

    /// <summary>Channels are bucketed to 8 levels (top three bits) so dithering does not read as content.</summary>
    private const int ChannelMask = 0xE0;

    /// <summary>Luma spread (0..255) below which a frame is considered blank.</summary>
    private const double BlankLumaRange = 4.0;

    /// <summary>Ceiling on the pixels a zoom may allocate; keeps a runaway --zoom a clear error, not an OOM.</summary>
    private const long MaxZoomPixels = 32L * 1024 * 1024;

    /// <summary>
    /// Copies a rectangle out of <paramref name="src"/>. The rectangle must lie fully inside the
    /// image; anything else is a <c>CaptureRegionOutsideImage</c> refusal (exit 2).
    /// </summary>
    public static Bitmap Crop(Bitmap src, int x, int y, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(src);

        if (!Fits(src, x, y, w, h))
        {
            throw new CliError(
                "CaptureRegionOutsideImage",
                $"Region x={x} y={y} w={w} h={h} does not fit inside the {src.Width}x{src.Height} capture.",
                exitCode: 2,
                context: new JsonObject
                {
                    ["requested"] = new JsonObject
                    {
                        ["x"] = x,
                        ["y"] = y,
                        ["width"] = w,
                        ["height"] = h,
                    },
                    ["image"] = new JsonObject
                    {
                        ["width"] = src.Width,
                        ["height"] = src.Height,
                    },
                },
                hints: new[]
                {
                    "Crop against the capture rect reported by the grab command (top-left origin, image pixels).",
                });
        }

        var region = new Rectangle(x, y, w, h);

        try
        {
            return src.Clone(region, src.PixelFormat);
        }
        catch (Exception)
        {
            // A few exotic pixel formats refuse a formatted clone; copy the pixels instead.
            var copy = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try
            {
                using var g = Graphics.FromImage(copy);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(src, new Rectangle(0, 0, w, h), region, GraphicsUnit.Pixel);
                return copy;
            }
            catch
            {
                copy.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Nearest-neighbour magnification by an integer <paramref name="factor"/> (1..8). Every source
    /// pixel becomes an exact <c>factor</c>x<paramref name="factor"/> block, so a coordinate read off
    /// the zoomed image divides back to the original image pixel without interpolation error.
    /// </summary>
    public static Bitmap Zoom(Bitmap src, int factor)
    {
        ArgumentNullException.ThrowIfNull(src);

        if (factor is < 1 or > 8)
        {
            throw new CliError(
                "InvalidArgument",
                $"Zoom factor must be between 1 and 8 (got {factor}).",
                exitCode: 1,
                hints: new[] { "Use --zoom 2 for a readable 2x enlargement." });
        }

        long dstWidth = (long)src.Width * factor;
        long dstHeight = (long)src.Height * factor;

        if (dstWidth * dstHeight > MaxZoomPixels)
        {
            throw new CliError(
                "InvalidArgument",
                $"Zooming {src.Width}x{src.Height} by {factor} would produce a {dstWidth}x{dstHeight} image, over the {MaxZoomPixels} pixel limit.",
                exitCode: 1,
                hints: new[] { "Crop the region first, then zoom it." });
        }

        var source = ReadPixels(src, out var width, out var height);
        var width1 = (int)dstWidth;
        var height1 = (int)dstHeight;
        var scaled = new int[width1 * height1];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * width;
            var dstRowBase = y * factor * width1;

            for (var dy = 0; dy < factor; dy++)
            {
                var dstRow = dstRowBase + (dy * width1);

                for (var x = 0; x < width; x++)
                {
                    var value = source[sourceRow + x];
                    var dstColumn = dstRow + (x * factor);

                    for (var dx = 0; dx < factor; dx++)
                    {
                        scaled[dstColumn + dx] = value;
                    }
                }
            }
        }

        return FromPixels(scaled, width1, height1);
    }

    /// <summary>
    /// Downscales so the longest side is at most <paramref name="maxSide"/> using HighQualityBicubic
    /// (the anchor option decides the exact pixel count). Never upscales, and always returns a new
    /// bitmap so the caller owns one uniform lifetime.
    /// </summary>
    public static Bitmap Fit(Bitmap src, int maxSide)
    {
        ArgumentNullException.ThrowIfNull(src);

        if (maxSide < 1)
        {
            throw new CliError(
                "InvalidArgument",
                $"Downscale target must be at least 1 pixel (got {maxSide}).",
                exitCode: 1,
                hints: new[] { "Use --max-side 1200 (0 or negative is refused)." });
        }

        var width = src.Width;
        var height = src.Height;
        var longest = Math.Max(width, height);

        if (longest <= maxSide)
        {
            return new Bitmap(src);
        }

        var scale = (double)maxSide / longest;
        var dstWidth = Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero));
        var dstHeight = Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));

        var dst = new Bitmap(dstWidth, dstHeight, PixelFormat.Format32bppArgb);

        try
        {
            using var g = Graphics.FromImage(dst);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;

            // TileFlipXY stops the bicubic kernel sampling past the edge (which leaves a pale
            // halo along the right and bottom border of the downscale).
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);

            g.DrawImage(
                src,
                new Rectangle(0, 0, dstWidth, dstHeight),
                0,
                0,
                width,
                height,
                GraphicsUnit.Pixel,
                attributes);

            return dst;
        }
        catch
        {
            dst.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Samples a grid of at most 24x24 pixels, buckets each channel to 8 levels and reports how many
    /// distinct colours there are plus the luma range. A frame is blank when it holds at most two
    /// distinct colours or its luma never moves by 4 levels. Never throws on tiny bitmaps.
    /// </summary>
    public static CaptureInspect Inspect(Bitmap bmp)
    {
        ArgumentNullException.ThrowIfNull(bmp);

        var width = bmp.Width;
        var height = bmp.Height;

        if (width <= 0 || height <= 0)
        {
            return new CaptureInspect(true, 0, 0, 0);
        }

        var columns = Math.Min(SampleAxis, width);
        var rows = Math.Min(SampleAxis, height);
        var colors = new HashSet<int>();
        var minLuma = double.MaxValue;
        var maxLuma = double.MinValue;

        for (var row = 0; row < rows; row++)
        {
            var y = rows == 1
                ? height / 2
                : (int)((long)row * (height - 1) / (rows - 1));

            for (var column = 0; column < columns; column++)
            {
                var x = columns == 1
                    ? width / 2
                    : (int)((long)column * (width - 1) / (columns - 1));

                var pixel = bmp.GetPixel(x, y);
                var key = ((pixel.R & ChannelMask) << 16) | ((pixel.G & ChannelMask) << 8) | (pixel.B & ChannelMask);
                colors.Add(key);

                var luma = (0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B);
                minLuma = Math.Min(minLuma, luma);
                maxLuma = Math.Max(maxLuma, luma);
            }
        }

        if (colors.Count == 0)
        {
            return new CaptureInspect(true, 0, 0, 0);
        }

        var blank = colors.Count <= 2 || (maxLuma - minLuma) < BlankLumaRange;
        return new CaptureInspect(blank, colors.Count, Math.Round(minLuma, 2), Math.Round(maxLuma, 2));
    }

    /// <summary>
    /// Writes <paramref name="bmp"/> to <paramref name="path"/>, creating parent directories as
    /// needed. <paramref name="format"/> is "png" (the default) or "jpg"; JPEG quality is 1..100 and
    /// is refused rather than silently clamped when out of range.
    /// </summary>
    public static void Save(Bitmap bmp, string path, string format, int quality)
    {
        ArgumentNullException.ThrowIfNull(bmp);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliError(
                "InvalidArgument",
                "Output path is empty.",
                exitCode: 1,
                hints: new[] { "Pass --out <file.png|file.jpg>." });
        }

        var jpeg = IsJpeg(format);

        if (jpeg && quality is < 1 or > 100)
        {
            throw new CliError(
                "InvalidArgument",
                $"JPEG quality must be between 1 and 100 (got {quality}).",
                exitCode: 1,
                hints: new[] { "Use --quality 90 for screenshots; 100 inflates the file for no readable gain." });
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliError("InvalidArgument", $"Output path '{path}' is not usable: {ex.Message}", exitCode: 1);
        }

        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (jpeg)
        {
            SaveJpeg(bmp, full, quality);
        }
        else
        {
            bmp.Save(full, ImageFormat.Png);
        }
    }

    /// <summary>File extension matching <paramref name="format"/>: ".png" or ".jpg".</summary>
    public static string Extension(string format) => IsJpeg(format) ? ".jpg" : ".png";

    /// <summary>True bounds test done in subtract form so a huge width cannot overflow.</summary>
    private static bool Fits(Bitmap src, int x, int y, int w, int h) =>
        w > 0
        && h > 0
        && x >= 0
        && y >= 0
        && x <= src.Width - w
        && y <= src.Height - h;

    /// <summary>Maps "jpg"/"jpeg" to JPEG, "png"/blank to PNG, and refuses anything else.</summary>
    private static bool IsJpeg(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        if (format.Equals("png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (format.Equals("jpg", StringComparison.OrdinalIgnoreCase)
            || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new CliError(
            "InvalidArgument",
            $"Unknown image format '{format}' (expected png or jpg).",
            exitCode: 1,
            hints: new[] { "Supported formats: png (default), jpg." });
    }

    private static void SaveJpeg(Bitmap bmp, string path, int quality)
    {
        var encoder = FindEncoder(ImageFormat.Jpeg);

        if (encoder is null)
        {
            // GDI+ always ships a JPEG codec on Windows; if it is somehow absent the default
            // encoder is still better than failing a save that already happened.
            bmp.Save(path, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        using var parameter = new EncoderParameter(Encoder.Quality, (long)quality);
        parameters.Param[0] = parameter;
        bmp.Save(path, encoder, parameters);
    }

    private static ImageCodecInfo? FindEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }

        return null;
    }

    /// <summary>Copies a bitmap into a packed 32bpp ARGB array, honouring a bottom-up stride.</summary>
    private static int[] ReadPixels(Bitmap bmp, out int width, out int height)
    {
        width = bmp.Width;
        height = bmp.Height;

        var pixels = new int[(long)width * height];
        var data = bmp.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = data.Stride;
            var row = new int[width];

            for (var y = 0; y < height; y++)
            {
                var scan = stride >= 0
                    ? data.Scan0 + (y * stride)
                    : data.Scan0 + ((height - 1 - y) * -stride);

                Marshal.Copy(scan, row, 0, width);
                Array.Copy(row, 0, pixels, (long)y * width, width);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return pixels;
    }

    /// <summary>Materialises a packed 32bpp ARGB array as a new bitmap the caller owns.</summary>
    private static Bitmap FromPixels(int[] pixels, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        try
        {
            var data = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var stride = data.Stride;
                var row = new int[width];

                for (var y = 0; y < height; y++)
                {
                    Array.Copy(pixels, (long)y * width, row, 0, width);

                    var scan = stride >= 0
                        ? data.Scan0 + (y * stride)
                        : data.Scan0 + ((height - 1 - y) * -stride);

                    Marshal.Copy(row, 0, scan, width);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }
}
