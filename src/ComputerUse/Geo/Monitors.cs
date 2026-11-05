using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ComputerUse.Cli;

namespace ComputerUse.Geo;

/// <summary>
/// One physical display. Rectangles are desktop coordinates in physical pixels; <c>Dpi</c> and
/// <c>ScalePercent</c> describe the effective scale the desktop composes it at.
/// </summary>
internal sealed record MonitorInfo(
    int Index,
    string Device,
    bool Primary,
    RectI Rect,
    RectI WorkArea,
    uint Dpi,
    int ScalePercent);

/// <summary>
/// The monitors of the desktop exactly as the window manager lays them out. Every rectangle is a
/// physical pixel rectangle in the virtual-screen coordinate space, so a point read off a screenshot
/// and a point handed to SendInput mean the same thing. The set is re-read on every call: displays
/// are hot-plugged, rearranged and rescaled while the process runs.
/// </summary>
internal static class Monitors
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private const uint MonitorInfoPrimary = 0x0000_0001;
    private const int MdtEffectiveDpi = 0;
    private const uint FallbackDpi = 96;

    /// <summary>Length of MONITORINFOEXW::szDevice in characters.</summary>
    private const int DeviceNameLength = 32;

    private const string DevicePathPrefix = @"\\.\";

    private static readonly string[] GapHints =
    [
        "the desktop is not a filled rectangle: the point is in a gap between monitors",
        "use screen info to see the monitor rectangles",
    ];

    private static readonly string[] NoMonitorHints =
    [
        "the desktop reported no monitors at all; this is a broken or disconnected session",
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// MONITORINFOEXW. <c>szDevice</c> is WCHAR[32] declared as raw bytes on purpose: a byte buffer is
    /// blittable, so the runtime can never re-marshal the field as an ANSI char array, which copies
    /// back only the first character of the device name.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public unsafe fixed byte szDevice[DeviceNameLength * 2];
    }

    /// <summary>A monitor as enumerated, before indices are assigned.</summary>
    private readonly record struct Candidate(string Device, bool Primary, RectI Rect, RectI WorkArea, uint Dpi);

    /// <summary>Callback sink; EnumDisplayMonitors cannot hand us a context pointer we can capture.</summary>
    [ThreadStatic]
    private static List<Candidate>? _sink;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern unsafe bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        delegate* unmanaged<nint, nint, nint, nint, int> lpfnEnum,
        nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEXW info);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Every monitor, sorted by device name in natural order (DISPLAY1, DISPLAY2, ... DISPLAY10) with
    /// <see cref="MonitorInfo.Index"/> assigned 0-based from that order.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> All()
    {
        var sink = new List<Candidate>(4);
        _sink = sink;
        try
        {
            unsafe
            {
                _ = EnumDisplayMonitors(0, 0, &MonitorEnumerator, 0);
            }
        }
        finally
        {
            _sink = null;
        }

        if (sink.Count == 0)
        {
            return Array.Empty<MonitorInfo>();
        }

        var ordered = sink.ToArray();
        Array.Sort(ordered, static (a, b) =>
        {
            var byName = NaturalCompare(a.Device, b.Device);
            if (byName != 0)
            {
                return byName;
            }

            var byX = a.Rect.X.CompareTo(b.Rect.X);
            return byX != 0 ? byX : a.Rect.Y.CompareTo(b.Rect.Y);
        });

        var result = new MonitorInfo[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var candidate = ordered[i];
            result[i] = new MonitorInfo(
                i,
                candidate.Device,
                candidate.Primary,
                candidate.Rect,
                candidate.WorkArea,
                candidate.Dpi,
                ScalePercent(candidate.Dpi));
        }

        return result;
    }

    /// <summary>The whole desktop as one physical pixel rectangle; may include gaps between monitors.</summary>
    public static RectI VirtualBounds() => new(
        GetSystemMetrics(SmXVirtualScreen),
        GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmCxVirtualScreen),
        GetSystemMetrics(SmCyVirtualScreen));

    /// <summary>
    /// The monitor whose rectangle contains the point, or null. When monitors mirror each other the
    /// primary wins; otherwise the first in natural device order.
    /// </summary>
    public static MonitorInfo? At(PointI p)
    {
        MonitorInfo? best = null;
        foreach (var monitor in All())
        {
            if (!monitor.Rect.Contains(p))
            {
                continue;
            }

            if (best is null || (monitor.Primary && !best.Primary))
            {
                best = monitor;
            }
        }

        return best;
    }

    /// <summary>Whether any monitor rectangle contains the point.</summary>
    public static bool AnyContains(PointI p) => At(p) is not null;

    /// <summary>The monitor nearest to the point by clamped distance; never null when a monitor exists.</summary>
    public static MonitorInfo Nearest(PointI p)
    {
        var monitors = All();
        if (monitors.Count == 0)
        {
            throw new CliError(
                "NoMonitors",
                "The desktop reported no monitors, so no point can be mapped to a screen.",
                2,
                hints: NoMonitorHints);
        }

        MonitorInfo nearest = monitors[0];
        var nearestDistance = DistanceSquared(p, nearest.Rect);
        for (var i = 1; i < monitors.Count; i++)
        {
            var distance = DistanceSquared(p, monitors[i].Rect);
            if (distance < nearestDistance)
            {
                nearest = monitors[i];
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>The monitor with the given index, or null when the index is out of range.</summary>
    public static MonitorInfo? ByIndex(int index)
    {
        if (index < 0)
        {
            return null;
        }

        foreach (var monitor in All())
        {
            if (monitor.Index == index)
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>
    /// The monitor named <c>DISPLAY3</c> or <c>\\.\DISPLAY3</c> (case-insensitive), or null.
    /// </summary>
    public static MonitorInfo? ByDevice(string device)
    {
        var wanted = NormalizeDevice(device);
        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (var monitor in All())
        {
            if (string.Equals(NormalizeDevice(monitor.Device), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>
    /// The monitor containing the point; throws <c>PointNotOnMonitor</c> (exit 2) when the point is in
    /// a gap between monitors, and says which monitor is nearest and how far away it is.
    /// </summary>
    public static MonitorInfo Require(PointI p)
    {
        var hit = At(p);
        if (hit is not null)
        {
            return hit;
        }

        var nearest = Nearest(p);
        var distance = Distance(p, nearest.Rect);

        var context = new JsonObject
        {
            ["point"] = new JsonObject
            {
                ["x"] = p.X,
                ["y"] = p.Y,
            },
            ["nearest"] = new JsonObject
            {
                ["index"] = nearest.Index,
                ["device"] = nearest.Device,
                ["rect"] = RectJson(nearest.Rect),
            },
            ["distance"] = distance,
        };

        throw new CliError(
            "PointNotOnMonitor",
            $"The point {p.X},{p.Y} is not on any monitor; the nearest monitor ({nearest.Device}) is {distance} px away.",
            2,
            context,
            GapHints);
    }

    [UnmanagedCallersOnly]
    private static int MonitorEnumerator(nint hMonitor, nint hdc, nint lprcMonitor, nint dwData)
    {
        var sink = _sink;
        if (sink is null)
        {
            return 0;
        }

        try
        {
            if (Describe(hMonitor) is { } candidate)
            {
                sink.Add(candidate);
            }
        }
        catch
        {
            // A monitor that cannot be described is skipped: an enumeration callback must never throw.
        }

        return 1;
    }

    private static unsafe Candidate? Describe(nint hMonitor)
    {
        var info = default(MONITORINFOEXW);
        info.cbSize = Marshal.SizeOf<MONITORINFOEXW>();

        if (!GetMonitorInfoW(hMonitor, ref info))
        {
            return null;
        }

        return new Candidate(
            DeviceName(info),
            (info.dwFlags & MonitorInfoPrimary) != 0,
            ToRect(info.rcMonitor),
            ToRect(info.rcWork),
            DpiOf(hMonitor));
    }

    private static unsafe string DeviceName(MONITORINFOEXW info)
    {
        // The struct is a stack value, so the buffer is already pinned: no fixed statement is needed.
        var chars = (char*)info.szDevice;

        var length = 0;
        while (length < DeviceNameLength && chars[length] != '\0')
        {
            length++;
        }

        if (length == 0)
        {
            return "";
        }

        var name = new string(chars, 0, length);
        return name.StartsWith(DevicePathPrefix, StringComparison.Ordinal)
            ? name[DevicePathPrefix.Length..]
            : name;
    }

    /// <summary>Effective DPI of the monitor; 96 when the query is unavailable.</summary>
    private static uint DpiOf(nint hMonitor)
    {
        try
        {
            if (GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            // shcore.dll is absent on very old Windows: fall back to the 96 DPI baseline below.
        }
        catch (EntryPointNotFoundException)
        {
            // Same fallback: the entry point is missing, not the display.
        }

        return FallbackDpi;
    }

    private static int ScalePercent(uint dpi) => (int)Math.Round(dpi * 100.0 / FallbackDpi, MidpointRounding.AwayFromZero);

    private static RectI ToRect(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static JsonObject RectJson(RectI rect) => new()
    {
        ["x"] = rect.X,
        ["y"] = rect.Y,
        ["width"] = rect.W,
        ["height"] = rect.H,
    };

    /// <summary>Squared distance from the point to the rectangle (0 when inside).</summary>
    private static long DistanceSquared(PointI p, RectI rect)
    {
        long dx = p.X < rect.X ? (long)rect.X - p.X
            : p.X >= rect.Right ? (long)p.X - rect.Right + 1
            : 0;
        long dy = p.Y < rect.Y ? (long)rect.Y - p.Y
            : p.Y >= rect.Bottom ? (long)p.Y - rect.Bottom + 1
            : 0;

        return (dx * dx) + (dy * dy);
    }

    private static int Distance(PointI p, RectI rect) => (int)Math.Round(Math.Sqrt(DistanceSquared(p, rect)));

    private static string NormalizeDevice(string? device)
    {
        var text = (device ?? "").Trim();
        if (text.StartsWith(DevicePathPrefix, StringComparison.Ordinal))
        {
            text = text[DevicePathPrefix.Length..];
        }

        return text.Trim();
    }

    /// <summary>
    /// Case-insensitive natural ordering so <c>DISPLAY2</c> sorts before <c>DISPLAY10</c>: runs of
    /// digits compare by numeric value, everything else by upper-cased character.
    /// </summary>
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;

        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                while (startA < i - 1 && a[startA] == '0')
                {
                    startA++;
                }

                while (startB < j - 1 && b[startB] == '0')
                {
                    startB++;
                }

                var lengthA = i - startA;
                var lengthB = j - startB;
                if (lengthA != lengthB)
                {
                    return lengthA - lengthB;
                }

                for (var k = 0; k < lengthA; k++)
                {
                    var digit = a[startA + k].CompareTo(b[startB + k]);
                    if (digit != 0)
                    {
                        return digit;
                    }
                }

                continue;
            }

            var left = char.ToUpperInvariant(a[i]);
            var right = char.ToUpperInvariant(b[j]);
            if (left != right)
            {
                return left.CompareTo(right);
            }

            i++;
            j++;
        }

        return (a.Length - i) - (b.Length - j);
    }
}
