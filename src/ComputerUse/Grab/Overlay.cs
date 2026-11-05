using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Grab;

/// <summary>
/// Paints coordinate annotations onto a finished bitmap — always after crop / zoom / downscale, so
/// line work stays pixel-crisp and labels keep their point size. Everything drawn here is in the
/// caller's space: label text is the number an agent can type straight back into a coordinate
/// option, not an image pixel index.
/// </summary>
internal static class Overlay
{
    /// <summary>Every fifth grid step is drawn heavier and brighter.</summary>
    private const int MajorEvery = 5;

    /// <summary>Lines closer than this are not drawn at all; the grid degrades to majors only.</summary>
    private const float MinLineSpacing = 6f;

    private const float ChipPadX = 3f;
    private const float ChipPadY = 1f;
    private const float LabelGap = 3f;
    private const long MaxLinesPerAxis = 4096;
    private const long MaxStride = 1_000_000_000L;

    private static readonly Color MinorStroke = Color.FromArgb(90, 0, 200, 0);
    private static readonly Color MajorStroke = Color.FromArgb(200, 40, 235, 40);
    private static readonly Color ChipBack = Color.FromArgb(220, 8, 8, 8);
    private static readonly Color ChipInk = Color.FromArgb(235, 226, 255, 226);
    private static readonly Color HaloStroke = Color.FromArgb(130, 0, 0, 0);

    /// <summary>
    /// Draws a coordinate grid over <paramref name="bmp"/> in place. <paramref name="spaceOrigin"/>
    /// is the space coordinate of image pixel (0,0) and <paramref name="pxPerPoint"/> is image pixels
    /// per space point, so a line lands exactly where the mapped coordinate falls. Tick labels carry
    /// the space value of their line and sit on a dark chip so they read over light and dark UI alike.
    /// </summary>
    public static void Grid(Bitmap bmp, PointI spaceOrigin, double pxPerPoint, int stepPoints)
    {
        Validate(bmp, pxPerPoint, out var width, out var height);

        if (stepPoints <= 0)
        {
            throw new CliError(
                "InvalidArgument",
                $"Grid step must be a positive number of space points (got {stepPoints}).",
                exitCode: 1,
                hints: new[] { "Use --grid 50 for a line every 50 space points." });
        }

        var stepPx = stepPoints * pxPerPoint;
        long stride = 1;

        // A sub-pixel grid is unreadable: thin it out by whole major intervals until lines are
        // distinguishable, then label only the lines that are actually drawn.
        while (stepPx * stride < MinLineSpacing && stride <= MaxStride / MajorEvery)
        {
            stride *= MajorEvery;
        }

        if (stepPx * stride < MinLineSpacing)
        {
            return;
        }

        var stepSpace = (long)stepPoints * stride;
        var vertical = AxisLines(width, spaceOrigin.X, pxPerPoint, stepSpace, stride);
        var horizontal = AxisLines(height, spaceOrigin.Y, pxPerPoint, stepSpace, stride);

        using var g = Graphics.FromImage(bmp);
        Configure(g);

        using var font = CreateFont(FontSizeFor(height));
        using var format = StringFormat.GenericTypographic;
        using var minorPen = new Pen(MinorStroke, 1f);
        using var majorPen = new Pen(MajorStroke, 2f);
        using var chipBrush = new SolidBrush(ChipBack);
        using var inkBrush = new SolidBrush(ChipInk);

        // Lines first, labels last: a chip must never be crossed by a later grid line.
        foreach (var line in vertical)
        {
            g.DrawLine(line.Major ? majorPen : minorPen, line.Position, 0f, line.Position, height - 1);
        }

        foreach (var line in horizontal)
        {
            g.DrawLine(line.Major ? majorPen : minorPen, 0f, line.Position, width - 1, line.Position);
        }

        var chipHeight = MathF.Ceiling(font.GetHeight(g)) + (ChipPadY * 2);
        var topBand = chipHeight + LabelGap + 2f;
        var chipWidthLimit = width - 1 - (LabelGap * 2);

        var lastRight = float.NegativeInfinity;

        foreach (var line in vertical)
        {
            if (chipWidthLimit <= 0)
            {
                break;
            }

            var chipWidth = ChipWidth(g, font, format, line.Text);

            // Prefer the right of the line; fold left when the right edge would clip it.
            var chipX = line.Position + LabelGap;
            if (chipX + chipWidth > width - 1)
            {
                chipX = line.Position - LabelGap - chipWidth;
            }

            if (chipX < 0 || chipX + chipWidth > width - 1 || chipX < lastRight + LabelGap)
            {
                continue;
            }

            DrawChip(g, chipBrush, inkBrush, font, format, line.Text, chipX, LabelGap, chipWidth, chipHeight);
            lastRight = chipX + chipWidth;
        }

        var lastBottom = float.NegativeInfinity;

        foreach (var line in horizontal)
        {
            if (chipWidthLimit <= 0)
            {
                break;
            }

            var chipWidth = ChipWidth(g, font, format, line.Text);
            if (chipWidth > chipWidthLimit)
            {
                continue;
            }

            var chipY = line.Position + LabelGap;
            if (chipY + chipHeight > height - 1)
            {
                chipY = line.Position - LabelGap - chipHeight;
            }

            // The top band belongs to the x labels; a y label there would be clipped or collide.
            if (chipY < topBand || chipY + chipHeight > height - 1 || chipY < lastBottom + LabelGap)
            {
                continue;
            }

            DrawChip(g, chipBrush, inkBrush, font, format, line.Text, LabelGap, chipY, chipWidth, chipHeight);
            lastBottom = chipY + chipHeight;
        }
    }

    /// <summary>
    /// Marks one coordinate on <paramref name="bmp"/> in place: a full crosshair or a small tick
    /// ring, plus an optional label chip. A point that maps outside the bitmap draws nothing and
    /// never throws — an off-screen mark is a fact about the capture, not a failure.
    /// </summary>
    public static void Mark(Bitmap bmp, PointI spaceOrigin, double pxPerPoint, PointI at, string? label, Color color, bool crosshair)
    {
        Validate(bmp, pxPerPoint, out var width, out var height);

        var rawX = (at.X - spaceOrigin.X) * pxPerPoint;
        var rawY = (at.Y - spaceOrigin.Y) * pxPerPoint;

        if (!double.IsFinite(rawX) || !double.IsFinite(rawY)
            || rawX < -1 || rawY < -1 || rawX > width || rawY > height)
        {
            return;
        }

        var cx = (int)Math.Round(rawX, MidpointRounding.AwayFromZero);
        var cy = (int)Math.Round(rawY, MidpointRounding.AwayFromZero);

        if (cx < 0 || cy < 0 || cx > width - 1 || cy > height - 1)
        {
            return;
        }

        using var g = Graphics.FromImage(bmp);
        Configure(g);

        using var font = CreateFont(FontSizeFor(height));
        using var format = StringFormat.GenericTypographic;
        using var haloPen = new Pen(HaloStroke, 3f);
        using var markPen = new Pen(color, 1.6f);
        using var markBrush = new SolidBrush(color);
        using var chipBrush = new SolidBrush(ChipBack);
        using var edgePen = new Pen(Color.FromArgb(190, color), 1f);
        using var inkBrush = new SolidBrush(ChipInk);

        var lineHeight = font.GetHeight(g);
        var radius = MathF.Max(7f, lineHeight * 0.7f);

        if (crosshair)
        {
            g.DrawLine(haloPen, cx, 0f, cx, height - 1);
            g.DrawLine(haloPen, 0f, cy, width - 1, cy);
            g.DrawLine(markPen, cx, 0f, cx, height - 1);
            g.DrawLine(markPen, 0f, cy, width - 1, cy);
        }
        else
        {
            var tick = MathF.Max(5f, radius * 0.6f);
            var ring = new RectangleF(cx - radius, cy - radius, radius * 2f, radius * 2f);

            g.DrawEllipse(haloPen, ring);
            g.DrawLine(haloPen, cx - radius - tick, cy, cx - radius, cy);
            g.DrawLine(haloPen, cx + radius, cy, cx + radius + tick, cy);
            g.DrawLine(haloPen, cx, cy - radius - tick, cx, cy - radius);
            g.DrawLine(haloPen, cx, cy + radius, cx, cy + radius + tick);

            g.DrawEllipse(markPen, ring);
            g.DrawLine(markPen, cx - radius - tick, cy, cx - radius, cy);
            g.DrawLine(markPen, cx + radius, cy, cx + radius + tick, cy);
            g.DrawLine(markPen, cx, cy - radius - tick, cx, cy - radius);
            g.DrawLine(markPen, cx, cy + radius, cx, cy + radius + tick);

            g.FillEllipse(markBrush, cx - 1.5f, cy - 1.5f, 3f, 3f);
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var text = label!.Trim();
        var chipWidth = ChipWidth(g, font, format, text);
        var chipHeight = MathF.Ceiling(lineHeight) + (ChipPadY * 2);
        var offset = (crosshair ? 0f : radius) + 6f;

        var x = cx + offset;
        var y = cy + 6f;

        if (x + chipWidth > width - 1)
        {
            x = cx - offset - chipWidth;
        }

        if (y + chipHeight > height - 1)
        {
            y = cy - 6f - chipHeight;
        }

        x = Math.Clamp(x, 0f, MathF.Max(0f, width - 1 - chipWidth));
        y = Math.Clamp(y, 0f, MathF.Max(0f, height - 1 - chipHeight));

        var chip = new RectangleF(MathF.Round(x), MathF.Round(y), chipWidth, chipHeight);
        g.FillRectangle(chipBrush, chip);
        g.DrawRectangle(edgePen, chip.X, chip.Y, chip.Width, chip.Height);
        g.DrawString(text, font, inkBrush, new PointF(chip.X + ChipPadX, chip.Y + ChipPadY), format);
    }

    /// <summary>Enumerates every grid line of one axis as (pixel position, major, label text).</summary>
    private static List<LineMark> AxisLines(int extent, int origin, double pxPerPoint, long stepSpace, long stride)
    {
        var lines = new List<LineMark>(64);

        if (extent < 1 || stepSpace < 1)
        {
            return lines;
        }

        var lastSpace = origin + ((extent - 1) / pxPerPoint);
        var first = (long)Math.Ceiling(origin / (double)stepSpace);

        for (long index = first, drawn = 0; drawn < MaxLinesPerAxis; index++, drawn++)
        {
            var space = index * (double)stepSpace;

            if (space > lastSpace)
            {
                break;
            }

            var px = (space - origin) * pxPerPoint;

            if (px < 0)
            {
                continue;
            }

            if (px > extent - 1)
            {
                break;
            }

            var major = (index * stride) % MajorEvery == 0;
            lines.Add(new LineMark(MathF.Round((float)px), major, FormatValue(space)));
        }

        return lines;
    }

    /// <summary>Space value of a grid line, rendered invariantly so the number reads back verbatim.</summary>
    private static string FormatValue(double space) =>
        ((long)Math.Round(space, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    private static float ChipWidth(Graphics g, Font font, StringFormat format, string text) =>
        MathF.Ceiling(g.MeasureString(text, font, PointF.Empty, format).Width) + (ChipPadX * 2) + 1f;

    private static void DrawChip(
        Graphics g,
        Brush back,
        Brush ink,
        Font font,
        StringFormat format,
        string text,
        float x,
        float y,
        float width,
        float height)
    {
        g.FillRectangle(back, x, y, width, height);
        g.DrawString(text, font, ink, new PointF(x + ChipPadX, y + ChipPadY), format);
    }

    /// <summary>Shared graphics setup: anti-aliased lines, ClearType labels, half-pixel line snapping.</summary>
    private static void Configure(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
    }

    /// <summary>Guards the mapping inputs and reports the bitmap extent.</summary>
    private static void Validate(Bitmap bmp, double pxPerPoint, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(bmp);

        if (!double.IsFinite(pxPerPoint) || pxPerPoint <= 0)
        {
            throw new CliError(
                "InvalidArgument",
                $"Scale must be a positive, finite number of image pixels per space point (got {pxPerPoint.ToString("R", CultureInfo.InvariantCulture)}).",
                exitCode: 1,
                hints: new[] { "The scale is derived from the zoom and downscale applied to the capture; 1.0 means one image pixel per space point." });
        }

        width = bmp.Width;
        height = bmp.Height;
    }

    /// <summary>
    /// Label size in points, mildly stepped by image height: readable on a thumbnail, not oversized
    /// on a 4K capture. The overlay is drawn after scaling, so this size is what the agent sees.
    /// </summary>
    private static float FontSizeFor(int height) => height <= 800 ? 10f : height <= 1600 ? 12f : 14f;

    /// <summary>Consolas with a fallback chain that never throws, whatever fonts the machine has.</summary>
    private static Font CreateFont(float size)
    {
        try
        {
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch (Exception)
        {
            // A missing family is normally substituted silently; keep the chain anyway so a broken
            // font cache can never fail a capture after the pixels were already gathered.
        }

        try
        {
            return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch (Exception)
        {
            return new Font(FontFamily.GenericMonospace, size, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    /// <summary>One grid line: snapped pixel position, whether it is a major step, and its label.</summary>
    private readonly record struct LineMark(float Position, bool Major, string Text);
}
