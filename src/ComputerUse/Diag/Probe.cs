using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Grab;
using ComputerUse.Input;

namespace ComputerUse.Diag;

/// <summary>
/// Live diagnosis. Everything a display arrangement can do to absolute pointer placement, measured
/// instead of assumed: per-monitor mapping error, cross-seam crossings, dead zones, capture sanity,
/// and the pointer settings that change the tool's own assumptions.
/// </summary>
internal static class Probe
{
    private const int SeamOffset = 24;
    private const int SeamPoints = 5;
    private const int ReversePoints = 3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, int[] value, uint winIni);

    public static int Run(string[] argv, int start)
    {
        Dispatch.LastCommand = "screen run-probe";
        var args = Args.Parse(argv, start, "screen run-probe", Usage.Screen, new HashSet<string>(StringComparer.Ordinal) { "--samples" }, new HashSet<string>(StringComparer.Ordinal));
        var samples = Math.Clamp(args.IntOr("--samples", 3), 1, 5);

        var watch = Stopwatch.StartNew();
        var monitors = Monitors.All();

        var clipAtStart = ClipCursor();

        var response = new Response("screen run-probe");
        var report = new JsonObject
        {
            ["awareness"] = Dpi.Awareness,
            ["virtualBounds"] = Out.Rect(Monitors.VirtualBounds()),
            ["monitors"] = MonitorArray(monitors),
            ["environment"] = Environment(),
            ["clipCursor"] = clipAtStart,
        };

        // Phase timings: "the probe got slower" should be answerable without guessing.
        var phase = Stopwatch.StartNew();

        report["mapping"] = Mapping(monitors, samples);
        var mappingMs = phase.ElapsedMilliseconds;

        phase.Restart();
        report["seams"] = Seams(monitors);
        var seamsMs = phase.ElapsedMilliseconds;

        phase.Restart();
        report["deadZones"] = DeadZones();
        var deadZoneMs = phase.ElapsedMilliseconds;

        phase.Restart();
        report["capture"] = CaptureChecks();
        var captureMs = phase.ElapsedMilliseconds;

        report["timings"] = new JsonObject
        {
            ["mappingMs"] = mappingMs,
            ["seamsMs"] = seamsMs,
            ["deadZoneMs"] = deadZoneMs,
            ["captureMs"] = captureMs,
        };

        // Read again: a clip rect can be set and released by whatever owns the
        // foreground, so a single reading at the start can contradict the probes.
        var clipAtEnd = ClipCursor();
        var clipChanged = clipAtStart?.ToJsonString() != clipAtEnd?.ToJsonString();
        if (clipChanged)
        {
            report["clipCursorPersisted"] = false;
            report["clipCursorAtEnd"] = clipAtEnd;
            report["clipCursorNote"] =
                "the cursor confinement differed between the start and the end of the probe, "
                + "so the probes above ran while it was being set or released";
        }
        else if (clipAtStart is not null)
        {
            report["clipCursorPersisted"] = true;
        }

        var verdict = Verdict(report);
        report["verdict"] = verdict;
        report["elapsedMs"] = watch.ElapsedMilliseconds;

        response.Root["report"] = report;
        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static JsonArray MonitorArray(IReadOnlyList<MonitorInfo> monitors)
    {
        var array = new JsonArray();
        foreach (var monitor in monitors)
        {
            array.Add((JsonNode)Out.Monitor(monitor));
        }

        return array;
    }

    private static JsonObject Environment()
    {
        var lbm = new JsonArray();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.Contains("LittleBigMouse", StringComparison.OrdinalIgnoreCase))
                    {
                        lbm.Add((JsonNode)$"{process.ProcessName} ({process.Id})");
                    }
                }
                catch
                {
                    // process vanished mid-enumeration
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // process enumeration is best effort
        }

        var environment = new JsonObject
        {
            ["pointerHooks"] = lbm,
        };

        var acceleration = new int[3];
        if (SystemParametersInfo(0x0003, 0, acceleration, 0))
        {
            environment["mouseThreshold1"] = acceleration[0];
            environment["mouseThreshold2"] = acceleration[1];
            environment["mouseAcceleration"] = acceleration[2];
        }

        if (SystemParametersInfo(0x0070, 0, out var speed, 0))
        {
            environment["pointerSpeed"] = speed;
        }

        if (SystemParametersInfo(0x005F, 0, out var snap, 0))
        {
            environment["snapToDefaultButton"] = snap != 0;
        }

        if (SystemParametersInfo(0x005E, 0, out var trails, 0))
        {
            environment["mouseTrails"] = trails;
        }

        if (SystemParametersInfo(0x1000, 0, out var activeTracking, 0))
        {
            environment["activeWindowTracking"] = activeTracking != 0;
        }

        return environment;
    }

    private static JsonNode? ClipCursor()
    {
        if (Pointer.ActiveClip() is not { } clip)
        {
            return null;
        }

        return new JsonObject
        {
            ["rect"] = Out.Rect(clip),
            ["note"] = "the cursor was confined to this rectangle when this was read",
        };
    }

    private static JsonObject Mapping(IReadOnlyList<MonitorInfo> monitors, int samples)
    {
        var perMonitor = new JsonArray();
        var worst = 0;
        var totalDelta = 0;
        var totalPoints = 0;

        foreach (var monitor in monitors)
        {
            var points = new JsonArray();
            var deltas = new List<int>();

            for (var iy = 1; iy <= samples; iy++)
            {
                for (var ix = 1; ix <= samples; ix++)
                {
                    var point = new PointI(
                        monitor.Rect.X + (monitor.Rect.W * ix / (samples + 1)),
                        monitor.Rect.Y + (monitor.Rect.H * iy / (samples + 1)));

                    var report = Pointer.Move(point, 1);
                    deltas.Add(report.Delta);
                    totalDelta += report.Delta;
                    totalPoints++;
                    worst = Math.Max(worst, report.Delta);

                    var mark = new JsonObject
                    {
                        ["requested"] = Out.Point(point),
                        ["landed"] = Out.Point(report.Landed),
                        ["delta"] = report.Delta,
                        ["strategy"] = report.Strategy,
                    };

                    if (report.ClipWaitMs > 0)
                    {
                        mark["clipWaitMs"] = report.ClipWaitMs;
                    }

                    if (report.Strategy == "clipped")
                    {
                        mark["clipped"] = true;
                        if (ClipCursor() is { } blocking)
                        {
                            mark["clipCursorAtFailure"] = blocking.DeepClone();
                        }
                    }

                    points.Add((JsonNode)mark);
                }
            }

            deltas.Sort();

            perMonitor.Add((JsonNode)new JsonObject
            {
                ["index"] = monitor.Index,
                ["device"] = monitor.Device,
                ["median"] = deltas.Count == 0 ? 0 : deltas[deltas.Count / 2],
                ["max"] = deltas.Count == 0 ? 0 : deltas[^1],
                ["points"] = points,
            });
        }

        return new JsonObject
        {
            ["perMonitor"] = perMonitor,
            ["max"] = worst,
            ["mean"] = totalPoints == 0 ? 0 : Math.Round(totalDelta / (double)totalPoints, 2),
        };
    }

    private static JsonArray Seams(IReadOnlyList<MonitorInfo> monitors)
    {
        var seams = new JsonArray();

        for (var i = 0; i < monitors.Count; i++)
        {
            for (var j = i + 1; j < monitors.Count; j++)
            {
                var a = monitors[i];
                var b = monitors[j];

                // A vertical seam is one x where two panels touch, and the overlap is the
                // stretch of y they share. Probing "the reverse direction" means walking
                // back across that same x, not jumping from the far edge of the other panel.
                if (a.Rect.Right == b.Rect.X || b.Rect.Right == a.Rect.X)
                {
                    var left = a.Rect.Right == b.Rect.X ? a : b;
                    var right = ReferenceEquals(left, a) ? b : a;
                    var seamX = left.Rect.Right;
                    var top = Math.Max(left.Rect.Y, right.Rect.Y);
                    var bottom = Math.Min(left.Rect.Bottom, right.Rect.Bottom);

                    if (bottom - top > SeamOffset * 4)
                    {
                        var forward = VerticalSeam(left.Index, right.Index, seamX, top, bottom, forward: true, SeamPoints);
                        seams.Add((JsonNode)forward);

                        if (Crossed(forward) < SeamPoints)
                        {
                            var reverse = VerticalSeam(right.Index, left.Index, seamX, top, bottom, forward: false, ReversePoints);
                            reverse["probedBecause"] = "the forward crossing did not always land";
                            seams.Add((JsonNode)reverse);
                        }
                    }
                }

                if (a.Rect.Bottom == b.Rect.Y || b.Rect.Bottom == a.Rect.Y)
                {
                    var topMonitor = a.Rect.Bottom == b.Rect.Y ? a : b;
                    var bottomMonitor = ReferenceEquals(topMonitor, a) ? b : a;
                    var seamY = topMonitor.Rect.Bottom;
                    var left = Math.Max(topMonitor.Rect.X, bottomMonitor.Rect.X);
                    var right = Math.Min(topMonitor.Rect.Right, bottomMonitor.Rect.Right);

                    if (right - left > SeamOffset * 4)
                    {
                        var forward = HorizontalSeam(topMonitor.Index, bottomMonitor.Index, seamY, left, right, forward: true, SeamPoints);
                        seams.Add((JsonNode)forward);

                        if (Crossed(forward) < SeamPoints)
                        {
                            var reverse = HorizontalSeam(bottomMonitor.Index, topMonitor.Index, seamY, left, right, forward: false, ReversePoints);
                            reverse["probedBecause"] = "the forward crossing did not always land";
                            seams.Add((JsonNode)reverse);
                        }
                    }
                }
            }
        }

        return seams;
    }

    /// <summary>
    /// Crosses <paramref name="seamX"/> from one side to the other at several heights. The seam
    /// coordinate is passed in rather than derived from a monitor, so the reverse direction walks
    /// back over the same line instead of jumping across the whole desktop.
    /// </summary>
    private static JsonObject VerticalSeam(int fromIndex, int toIndex, int seamX, int top, int bottom, bool forward, int points)
    {
        var marks = new JsonArray();
        var crossed = 0;

        for (var k = 1; k <= points; k++)
        {
            var y = top + ((bottom - top) * k / (points + 1));
            var from = new PointI(forward ? seamX - SeamOffset : seamX + SeamOffset, y);
            var to = new PointI(forward ? seamX + SeamOffset : seamX - SeamOffset, y);

            _ = Pointer.Move(from, 1);
            var report = Pointer.Move(to, 1);
            if (report.Delta <= 2)
            {
                crossed++;
            }

            marks.Add((JsonNode)Crossing(from, to, report));
        }

        return new JsonObject
        {
            ["kind"] = "vertical",
            ["direction"] = forward ? "forward" : "reverse",
            ["from"] = fromIndex,
            ["to"] = toIndex,
            ["x"] = seamX,
            ["points"] = points,
            ["crossed"] = crossed,
            ["marks"] = marks,
        };
    }

    private static JsonObject HorizontalSeam(int fromIndex, int toIndex, int seamY, int left, int right, bool forward, int points)
    {
        var marks = new JsonArray();
        var crossed = 0;

        for (var k = 1; k <= points; k++)
        {
            var x = left + ((right - left) * k / (points + 1));
            var from = new PointI(x, forward ? seamY - SeamOffset : seamY + SeamOffset);
            var to = new PointI(x, forward ? seamY + SeamOffset : seamY - SeamOffset);

            _ = Pointer.Move(from, 1);
            var report = Pointer.Move(to, 1);
            if (report.Delta <= 2)
            {
                crossed++;
            }

            marks.Add((JsonNode)Crossing(from, to, report));
        }

        return new JsonObject
        {
            ["kind"] = "horizontal",
            ["direction"] = forward ? "forward" : "reverse",
            ["from"] = fromIndex,
            ["to"] = toIndex,
            ["y"] = seamY,
            ["points"] = points,
            ["crossed"] = crossed,
            ["marks"] = marks,
        };
    }

    private static JsonObject Crossing(PointI from, PointI to, MoveReport report)
    {
        var mark = new JsonObject
        {
            ["from"] = Out.Point(from),
            ["requested"] = Out.Point(to),
            ["landed"] = Out.Point(report.Landed),
            ["delta"] = report.Delta,
            ["strategy"] = report.Strategy,
        };

        // When a crossing lands short, whether a clip rectangle was in force decides the
        // explanation: a clip that excludes the target makes the move impossible, and a hook
        // that repositions the cursor leaves no clip behind.
        if (report.Delta > 2)
        {
            var clip = ClipCursor();
            if (clip is not null)
            {
                mark["clipCursorAtFailure"] = clip.DeepClone();
            }
        }

        return mark;
    }

    private static JsonArray DeadZones()
    {
        var bounds = Monitors.VirtualBounds();
        var step = Math.Max(48, Math.Max(bounds.W, bounds.H) / 60);

        // Collect candidates over the whole desktop first: stopping at the first
        // five would only ever report one corner of one gap.
        var candidates = new List<PointI>();
        for (var y = bounds.Y; y < bounds.Bottom; y += step)
        {
            for (var x = bounds.X; x < bounds.Right; x += step)
            {
                var point = new PointI(x, y);
                if (!Monitors.AnyContains(point))
                {
                    candidates.Add(point);
                }
            }
        }

        var found = new JsonArray();
        if (candidates.Count == 0)
        {
            return found;
        }

        var picks = Math.Min(5, candidates.Count);
        for (var i = 0; i < picks; i++)
        {
            var point = candidates[(int)((long)i * candidates.Count / picks)];
            var rejected = false;
            string? code = null;

            try
            {
                _ = Monitors.Require(point);
            }
            catch (CliError error)
            {
                rejected = true;
                code = error.Code;
            }

            found.Add((JsonNode)new JsonObject
            {
                ["point"] = Out.Point(point),
                ["rejected"] = rejected,
                ["code"] = code,
            });
        }

        return found;
    }

    private static JsonObject CaptureChecks()
    {
        var checks = new JsonObject();

        var foreground = WindowList.List().FirstOrDefault(w => w.Foreground);
        if (foreground is not null)
        {
            var entry = new JsonObject
            {
                ["hwnd"] = foreground.Hwnd,
                ["rect"] = Out.Rect(foreground.Rect),
            };

            try
            {
                using var captured = WindowGrab.Capture(foreground);
                entry["source"] = captured.Source;
                entry["image"] = new JsonObject
                {
                    ["width"] = captured.Image.Width,
                    ["height"] = captured.Image.Height,
                };
                entry["pxPerPoint"] = Math.Round(captured.NativePxPerPoint, 4);
                entry["blank"] = ImageFx.Inspect(captured.Image).Blank;
            }
            catch (CliError error)
            {
                entry["error"] = error.Code;
                entry["message"] = error.Message;
            }

            checks["window"] = entry;
        }

        var pointer = Pointer.Get();
        var monitor = Monitors.At(pointer);
        if (monitor is not null)
        {
            using var captured = MonitorGrab.Capture(monitor.Rect);
            checks["monitor"] = new JsonObject
            {
                ["index"] = monitor.Index,
                ["rect"] = Out.Rect(monitor.Rect),
                ["image"] = new JsonObject
                {
                    ["width"] = captured.Image.Width,
                    ["height"] = captured.Image.Height,
                },
                ["sizeMatches"] = captured.Image.Width == monitor.Rect.W && captured.Image.Height == monitor.Rect.H,
                ["blank"] = ImageFx.Inspect(captured.Image).Blank,
            };
        }

        return checks;
    }

    private static int Crossed(JsonObject seam) => (int)(seam["crossed"]?.GetValue<int>() ?? 0);

    private static string Verdict(JsonObject report)
    {
        var mapping = report["mapping"]!;
        var worst = (int)(mapping["max"]?.GetValue<int>() ?? 0);
        var seams = report["seams"]!;

        var seamFailures = 0;
        if (seams is JsonArray seamArray)
        {
            foreach (var seam in seamArray)
            {
                if (seam is JsonObject entry && (int)(entry["crossed"]?.GetValue<int>() ?? 0) == 0)
                {
                    seamFailures++;
                }
            }
        }

        if (worst == 0 && seamFailures == 0)
        {
            return "exact";
        }

        // A clipped cursor explains a failure completely: the pointer was confined to a
        // rectangle that excludes the target, so no strategy could have landed it.
        var clipped = 0;
        if (seams is JsonArray seamList)
        {
            foreach (var seam in seamList)
            {
                if (seam is not JsonObject entry || entry["marks"] is not JsonArray marks)
                {
                    continue;
                }

                foreach (var mark in marks)
                {
                    if (mark is JsonObject point && point["clipCursorAtFailure"] is not null)
                    {
                        clipped++;
                    }
                }
            }
        }

        if (mapping["perMonitor"] is JsonArray perMonitor)
        {
            foreach (var monitor in perMonitor)
            {
                if (monitor is not JsonObject entry || entry["points"] is not JsonArray points)
                {
                    continue;
                }

                foreach (var point in points)
                {
                    if (point is JsonObject mark && mark["clipped"] is not null)
                    {
                        clipped++;
                    }
                }
            }
        }

        if (clipped > 0)
        {
            return "clipped-by-hook";
        }

        if (seamFailures > 0)
        {
            return "blocked-at-seam";
        }

        return worst <= 2 ? "approximate-within-tolerance" : "remapped";
    }
}
