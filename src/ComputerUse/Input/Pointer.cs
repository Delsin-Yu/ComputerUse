using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Input;

internal sealed record MoveReport(PointI Requested, PointI Landed, int Attempts, string Strategy)
{
    /// <summary>Milliseconds spent waiting for a cursor clip to release before giving up.</summary>
    public int ClipWaitMs { get; init; }

    public int Delta => Math.Max(Math.Abs(Landed.X - Requested.X), Math.Abs(Landed.Y - Requested.Y));

    public bool Ok => Delta == 0;
}

/// <summary>
/// Closed-loop absolute pointer movement. Every move is read back and compared with the target;
/// when the machine puts the cursor somewhere else (DPI scaling, a low-level hook, a monitor edge)
/// the strategies escalate and the eventual failure names the likely cause instead of pretending
/// the click happened.
/// </summary>
internal static class Pointer
{
    private const int PollIntervalMs = 4;
    private const int StableReads = 3;
    private const int PollBudgetMs = 400;
    private const int ClipWaitBudgetMs = 400;
    private const int ClipPollMs = 20;

    public static PointI Get()
    {
        return NativeInput.GetCursorPos(out var point) ? new PointI(point.X, point.Y) : new PointI(0, 0);
    }

    /// <summary>Reads the cursor until it stops moving, then returns it.</summary>
    public static PointI WaitStable()
    {
        var deadline = Stopwatch.StartNew();
        var last = Get();
        var stable = 0;

        while (deadline.ElapsedMilliseconds < PollBudgetMs)
        {
            Thread.Sleep(PollIntervalMs);
            var current = Get();
            if (current == last)
            {
                if (++stable >= StableReads - 1)
                {
                    return current;
                }
            }
            else
            {
                stable = 0;
                last = current;
            }
        }

        return Get();
    }

    /// <summary>Moves the cursor, escalating strategies, and returns where it actually landed.</summary>
    public static MoveReport Move(PointI target, int tolerance)
    {
        if (!Monitors.AnyContains(target))
        {
            throw NotOnMonitor(target);
        }

        // A clip can be already in force when the move starts; it is often released a
        // moment later, so give it a short chance before declaring the point unreachable.
        var clipWaitMs = 0;
        if (ActiveClip() is { } pending && !pending.Contains(target))
        {
            var released = WaitForClipRelease(target, ClipWaitBudgetMs);
            if (released < 0)
            {
                return new MoveReport(target, Get(), 1, "clipped") { ClipWaitMs = ClipWaitBudgetMs };
            }

            clipWaitMs = released;
        }

        var origin = Get();
        var attempts = 0;
        var landed = origin;

        foreach (var strategy in new (string Name, Action<PointI> Apply)[]
                 {
                     ("sendinput", AbsoluteSend),
                     ("sendinput-retry", target =>
                     {
                         // A hook can swallow one move; asking again is cheaper than walking.
                         Thread.Sleep(30);
                         AbsoluteSend(target);
                     }),
                     ("setcursorpos", SetPosition),
                     ("waypoints", WalkTo),
                     ("route", RouteTo),
                 })
        {
            attempts++;
            strategy.Apply(target);
            landed = WaitStable();

            if (MaxDistance(landed, target) <= tolerance)
            {
                return new MoveReport(target, landed, attempts, strategy.Name);
            }

            // A cursor that is clipped to a rectangle excluding the target cannot be moved
            // there by any strategy — but the clip is frequently transient, so wait for it
            // instead of burning the walk and route attempts (and say how long we waited).
            if (ActiveClip() is { } clip && !clip.Contains(target))
            {
                var released = WaitForClipRelease(target, ClipWaitBudgetMs);
                if (released < 0)
                {
                    return new MoveReport(target, landed, attempts, "clipped")
                    {
                        ClipWaitMs = clipWaitMs + ClipWaitBudgetMs,
                    };
                }

                clipWaitMs += released;
            }
        }

        return new MoveReport(target, landed, attempts, "failed");
    }

    /// <summary>Moves or throws with a classified diagnosis.</summary>
    public static MoveReport Require(PointI target, int tolerance, string action)
    {
        var report = Move(target, tolerance);
        if (MaxDistance(report.Landed, target) > tolerance)
        {
            var hints = Diagnose(report.Requested, report.Landed, out var causes);

            throw new CliError(
                "PointerNotLanded",
                $"{action}: the cursor did not reach the target ({report.Landed} instead of {target}, "
                + $"{report.Delta} px off after {report.Attempts} strategies).",
                context: new JsonObject
                {
                    ["requested"] = Out.Point(report.Requested),
                    ["landed"] = Out.Point(report.Landed),
                    ["delta"] = report.Delta,
                    ["attempts"] = report.Attempts,
                    ["strategy"] = report.Strategy,
                    ["landedMonitor"] = Monitors.At(report.Landed)?.Index,
                    ["causes"] = Out.Strings(causes),
                },
                hints: hints);
        }

        return report;
    }

    public static CliError NotOnMonitor(PointI target)
    {
        var nearest = Monitors.Nearest(target);
        var distance = MaxDistance(nearest.Rect.Center, target);

        return new CliError(
            "PointNotOnMonitor",
            $"({target.X},{target.Y}) is not inside any monitor rectangle.",
            context: new JsonObject
            {
                ["point"] = Out.Point(target),
                ["nearest"] = new JsonObject
                {
                    ["index"] = nearest.Index,
                    ["device"] = nearest.Device,
                    ["rect"] = Out.Rect(nearest.Rect),
                },
                ["distance"] = distance,
            },
            hints:
            [
                "the desktop is not a filled rectangle: this point is in a gap between monitors",
                "run 'screen info' to see the monitor rectangles",
            ]);
    }

    public static string[] Diagnose(PointI requested, PointI landed, out string[] causes)
    {
        if (ActiveClip() is { } clip && !clip.Contains(requested))
        {
            causes = ["cursor-clipped"];
            return
            [
                $"the cursor is confined to {clip.X},{clip.Y} {clip.W}x{clip.H} and the target is outside it, "
                + "so no move can land there while that clip is in force",
                "the clip is set by whatever owns the pointer: a game, a remote-desktop client, or a mouse manager",
                "retry when the clip is released; if it never is, that rectangle genuinely cannot be reached",
            ];
        }

        if (!Monitors.AnyContains(landed))
        {
            causes = ["dead-zone"];
            return
            [
                "the cursor parked in a gap between monitors, where nothing is drawn and a click would go nowhere",
                "the requested point is valid, so an input hook or a coordinate-space mismatch moved it",
            ];
        }

        var monitor = Monitors.At(landed);
        if (monitor is { } m && MaxDistance(landed, m.Rect.Center) >= 12)
        {
            const int edge = 4;
            var onEdge = Math.Abs(landed.X - m.Rect.X) <= edge
                         || Math.Abs(landed.X - m.Rect.Right) <= edge
                         || Math.Abs(landed.Y - m.Rect.Y) <= edge
                         || Math.Abs(landed.Y - m.Rect.Bottom) <= edge;

            if (onEdge && !m.Rect.Contains(requested))
            {
                causes = ["edge-crossing"];
                return
                [
                    "the cursor stopped on a monitor edge while the target is on another monitor: the crossing was refused or mangled",
                    "an input hook (for example a multi-monitor mouse manager) is the usual cause",
                ];
            }
        }

        foreach (var factor in new[] { 1.25, 1.5, 1.75, 2.0 })
        {
            var scaled = new PointI((int)Math.Round(requested.X * factor), (int)Math.Round(requested.Y * factor));
            if (MaxDistance(scaled, landed) <= 3 || MaxDistance(requested, new PointI((int)Math.Round(landed.X / factor), (int)Math.Round(landed.Y / factor))) <= 3)
            {
                causes = ["dpi-scale"];
                return
                [
                    $"the landing point looks like a DPI scale factor of {factor} was applied twice",
                    "the tool runs per-monitor-v2; something between here and the cursor is scaling coordinates",
                ];
            }
        }

        causes = ["input-hook"];
        return
        [
            "no simple cause matched: the cursor was moved by something else (input hook, pointer clamp, or a window that repositions the pointer)",
        ];
    }

    public static int MaxDistance(PointI a, PointI b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Waits until the clip stops excluding the target; -1 when it never did.</summary>
    private static int WaitForClipRelease(PointI target, int budgetMs)
    {
        var watch = Stopwatch.StartNew();

        while (watch.ElapsedMilliseconds < budgetMs)
        {
            Thread.Sleep(ClipPollMs);

            if (ActiveClip() is not { } clip || clip.Contains(target))
            {
                return (int)watch.ElapsedMilliseconds;
            }
        }

        return -1;
    }

    /// <summary>The rectangle the cursor is confined to right now, or null when it is free.</summary>
    public static RectI? ActiveClip()
    {
        if (!NativeInput.GetClipCursor(out var rect))
        {
            return null;
        }

        if (rect.Left == 0 && rect.Top == 0 && rect.Right == 0 && rect.Bottom == 0)
        {
            return null;
        }

        var clip = new RectI(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return clip == Monitors.VirtualBounds() ? null : clip;
    }

    /// <summary>One absolute move with no verification, for intermediate drag steps.</summary>
    public static void Jump(PointI target) => AbsoluteSend(target);

    private static void AbsoluteSend(PointI target)
    {
        var bounds = Monitors.VirtualBounds();
        if (bounds.W <= 1 || bounds.H <= 1)
        {
            throw new CliError("Win32Error", "Virtual screen metrics are invalid.");
        }

        var ax = (int)Math.Round((target.X - bounds.X) * 65535.0 / (bounds.W - 1));
        var ay = (int)Math.Round((target.Y - bounds.Y) * 65535.0 / (bounds.H - 1));

        var clampedX = Math.Clamp(ax, 0, 65535);
        var clampedY = Math.Clamp(ay, 0, 65535);

        NativeInput.Send(NativeInput.MouseEvent(
            NativeInput.MouseMove | NativeInput.MouseAbsolute | NativeInput.MouseVirtualDesk,
            clampedX,
            clampedY));
    }

    private static void SetPosition(PointI target)
    {
        _ = NativeInput.SetCursorPos(target.X, target.Y);
    }

    private static void WalkTo(PointI target)
    {
        var origin = Get();
        var dx = target.X - origin.X;
        var dy = target.Y - origin.Y;
        var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (distance <= 1)
        {
            AbsoluteSend(target);
            return;
        }

        var steps = Math.Clamp(distance / 16, 2, 24);
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var point = new PointI(
                (int)Math.Round(origin.X + dx * t),
                (int)Math.Round(origin.Y + dy * t));

            AbsoluteSend(point);
            Thread.Sleep(2);
        }
    }

    /// <summary>Walks through the monitor centres so a straight jump cannot be stopped by a seam.</summary>
    private static void RouteTo(PointI target)
    {
        var from = Get();
        var startMonitor = Monitors.At(from);
        var targetMonitor = Monitors.At(target);

        if (startMonitor is null || targetMonitor is null)
        {
            WalkTo(target);
            return;
        }

        if (startMonitor.Index == targetMonitor.Index)
        {
            WalkTo(target);
            return;
        }

        AbsoluteSend(startMonitor.Rect.Center);
        Thread.Sleep(4);
        AbsoluteSend(targetMonitor.Rect.Center);
        Thread.Sleep(4);
        WalkTo(target);
    }
}
