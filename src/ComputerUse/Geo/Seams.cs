namespace ComputerUse.Geo;

/// <summary>Which axis a shared edge runs along: a vertical seam is crossed left/right.</summary>
internal enum SeamKind
{
    Vertical,
    Horizontal,
}

/// <summary>
/// A shared edge between two touching monitors: the line itself plus the stretch of it the two
/// panels have in common. Everything a planned crossing needs, and nothing about how it is probed.
/// </summary>
internal sealed record Seam(
    SeamKind Kind,
    int From,
    int To,
    int Edge,
    int OverlapStart,
    int OverlapEnd,
    bool FromIsLow)
{
    public bool IsVertical => Kind == SeamKind.Vertical;

    /// <summary>Coordinate along the seam, clamped away from the ends where the panels stop.</summary>
    public int Along(int wanted, int margin = 8)
    {
        var low = OverlapStart + margin;
        var high = OverlapEnd - margin;
        return high <= low ? (OverlapStart + OverlapEnd) / 2 : Math.Clamp(wanted, low, high);
    }

    /// <summary>The point just before the seam and the point just past it, plus the crossing itself.</summary>
    public (PointI Approach, PointI Land, PointI OnSeam) Points(int offset, int along)
    {
        if (IsVertical)
        {
            var approachX = FromIsLow ? Edge - offset : Edge + offset;
            var landX = FromIsLow ? Edge + offset : Edge - offset;
            return (new PointI(approachX, along), new PointI(landX, along), new PointI(Edge, along));
        }

        var approachY = FromIsLow ? Edge - offset : Edge + offset;
        var landY = FromIsLow ? Edge + offset : Edge - offset;
        return (new PointI(along, approachY), new PointI(along, landY), new PointI(along, Edge));
    }
}

/// <summary>
/// The monitor adjacency graph. Two panels are adjacent when their rectangles share an edge over a
/// usable stretch; anything else (a corner, a gap) is not a seam, and a move that needs to pass it
/// has to route around instead of teleporting through empty desktop.
/// </summary>
internal static class Seams
{
    private const int MinimumOverlap = 24;

    public static IReadOnlyList<Seam> Between(IReadOnlyList<MonitorInfo> monitors)
    {
        var seams = new List<Seam>();

        for (var i = 0; i < monitors.Count; i++)
        {
            for (var j = i + 1; j < monitors.Count; j++)
            {
                var a = monitors[i];
                var b = monitors[j];

                if (TryVertical(a, b, out var vertical))
                {
                    seams.Add(vertical!);
                }

                if (TryHorizontal(a, b, out var horizontal))
                {
                    seams.Add(horizontal!);
                }
            }
        }

        return seams;
    }

    /// <summary>Monitor indices to walk through from one panel to another, or empty when unreachable.</summary>
    public static List<int> Path(IReadOnlyList<MonitorInfo> monitors, int from, int to)
    {
        if (from == to)
        {
            return [from];
        }

        var seams = Between(monitors);
        var previous = new Dictionary<int, int> { [from] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var seam in seams)
            {
                int next;
                if (seam.From == current)
                {
                    next = seam.To;
                }
                else if (seam.To == current)
                {
                    next = seam.From;
                }
                else
                {
                    continue;
                }

                if (previous.ContainsKey(next))
                {
                    continue;
                }

                previous[next] = current;

                if (next == to)
                {
                    var path = new List<int>();
                    for (var step = to; step != -1; step = previous[step])
                    {
                        path.Add(step);
                    }

                    path.Reverse();
                    return path;
                }

                queue.Enqueue(next);
            }
        }

        return [];
    }

    /// <summary>The seam joining two given monitors, oriented to cross from <paramref name="from"/>.</summary>
    public static Seam? Find(IReadOnlyList<MonitorInfo> monitors, int from, int to)
    {
        var a = monitors.FirstOrDefault(m => m.Index == from);
        var b = monitors.FirstOrDefault(m => m.Index == to);
        if (a is null || b is null)
        {
            return null;
        }

        if (TryVertical(a, b, out var vertical))
        {
            return vertical!.From == from ? vertical : WithDirection(vertical, from, to);
        }

        if (TryHorizontal(a, b, out var horizontal))
        {
            return horizontal!.From == from ? horizontal : WithDirection(horizontal, from, to);
        }

        return null;
    }

    private static Seam WithDirection(Seam seam, int from, int to) =>
        seam with { From = from, To = to, FromIsLow = !seam.FromIsLow };

    private static bool TryVertical(MonitorInfo a, MonitorInfo b, out Seam? seam)
    {
        seam = null;
        int leftIndex, rightIndex, edge;
        MonitorInfo left, right;

        if (a.Rect.Right == b.Rect.X)
        {
            left = a;
            right = b;
            leftIndex = a.Index;
            rightIndex = b.Index;
        }
        else if (b.Rect.Right == a.Rect.X)
        {
            left = b;
            right = a;
            leftIndex = b.Index;
            rightIndex = a.Index;
        }
        else
        {
            return false;
        }

        edge = left.Rect.Right;
        var top = Math.Max(left.Rect.Y, right.Rect.Y);
        var bottom = Math.Min(left.Rect.Bottom, right.Rect.Bottom);

        if (bottom - top < MinimumOverlap)
        {
            return false;
        }

        seam = new Seam(SeamKind.Vertical, leftIndex, rightIndex, edge, top, bottom, FromIsLow: true);
        return true;
    }

    private static bool TryHorizontal(MonitorInfo a, MonitorInfo b, out Seam? seam)
    {
        seam = null;
        MonitorInfo top, bottomMonitor;
        int topIndex, bottomIndex;

        if (a.Rect.Bottom == b.Rect.Y)
        {
            top = a;
            bottomMonitor = b;
            topIndex = a.Index;
            bottomIndex = b.Index;
        }
        else if (b.Rect.Bottom == a.Rect.Y)
        {
            top = b;
            bottomMonitor = a;
            topIndex = b.Index;
            bottomIndex = a.Index;
        }
        else
        {
            return false;
        }

        var edge = top.Rect.Bottom;
        var left = Math.Max(top.Rect.X, bottomMonitor.Rect.X);
        var right = Math.Min(top.Rect.Right, bottomMonitor.Rect.Right);

        if (right - left < MinimumOverlap)
        {
            return false;
        }

        seam = new Seam(SeamKind.Horizontal, topIndex, bottomIndex, edge, left, right, FromIsLow: true);
        return true;
    }
}
