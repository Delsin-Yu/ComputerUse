using System.Drawing;
using System.Drawing.Imaging;
using ComputerUse.Cli.Json;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Uia;

internal static class SnapshotAnnotator
{
    public static ScreenshotDto CaptureAnnotated(nint hwnd, WindowSnapshotDto snapshot, string pathArgument)
    {
        var region = ScreenService.FromHwnd(hwnd);

        var outputPath = ScreenshotService.ResolveOutputPath(pathArgument);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = ScreenshotService.CaptureRegionBitmap(region);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var boxPen = new Pen(Color.FromArgb(220, 255, 80, 0), 2f);
        using var fillBrush = new SolidBrush(Color.FromArgb(200, 255, 80, 0));
        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);

        if (snapshot.Elements is not null)
        {
            foreach (var (refId, element) in snapshot.Elements)
            {
                if (element.Bounds.Width <= 1 || element.Bounds.Height <= 1)
                {
                    continue;
                }

                var localX = element.Bounds.X - region.X;
                var localY = element.Bounds.Y - region.Y;
                if (localX + element.Bounds.Width < 0
                    || localY + element.Bounds.Height < 0
                    || localX > region.Width
                    || localY > region.Height)
                {
                    continue;
                }

                var box = new Rectangle(localX, localY, element.Bounds.Width, element.Bounds.Height);
                graphics.DrawRectangle(boxPen, box);

                var label = refId;
                var size = graphics.MeasureString(label, font);
                var labelRect = new RectangleF(
                    Math.Clamp(localX, 0, Math.Max(0, region.Width - (int)size.Width - 2)),
                    Math.Clamp(localY - (int)size.Height - 1, 0, Math.Max(0, region.Height - (int)size.Height - 1)),
                    size.Width + 4,
                    size.Height + 2);
                graphics.FillRectangle(fillBrush, labelRect);
                graphics.DrawString(label, font, textBrush, labelRect.X + 2, labelRect.Y + 1);
            }
        }

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
}
