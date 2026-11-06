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

    /// <summary>The pointer is pinned: different targets keep being refused by the same clip.</summary>
    public bool Frozen { get; init; }

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
    private const int ClipWaitBudgetMs = 500;
    private const int ClipPollMs = 20;

    /// <summary>How far either side of a shared edge a planned crossing steps.</summary>
    private const int SeamStep = 24;

    /// <summary>Upper bound on relative moves spent trying to release a held clip.</summary>
    private const int NudgeMax = 12;

    /// <summary>Size of one relative nudge, in pixels.</summary>
    private const int NudgeStep = 40;

    /// <summary>
    /// How many different, refused targets make a pinned pointer: three moves that asked for
    /// three different points and were each held by a clip that the nudges never lifted cannot
    /// be a coincidence, and paying the 500 ms budget again for every further point costs
    /// seconds and hides the cause.
    /// </summary>
    private const int FreezeSamples = 3;

    private static readonly Queue<(PointI Requested, PointI Landed)> ClipFailures = new();
    private static bool pinned;

    /// <summary>Records a refused move; true when the pointer has been pinned by the same clip.</summary>
    private static bool RegisterClipFailure(PointI requested, PointI landed)
    {
        ClipFailures.Enqueue((requested, landed));
        while (ClipFailures.Count > FreezeSamples)
        {
            ClipFailures.Dequeue();
        }

        if (ClipFailures.Count < FreezeSamples)
        {
            return false;
        }

        var items = ClipFailures.ToArray();

        // The same target retried is not a freeze; different targets landing on one pixel is.
        if (items.Select(item => item.Requested).Distinct().Count() < FreezeSamples)
        {
            return false;
        }

        pinned = true;
        return true;
    }

    private static void ClearFreeze()
    {
        pinned = false;
        ClipFailures.Clear();
    }

    /// <summary>Motion first, teleport second: a hook that holds the pointer resets on movement.</summary>
    private static void Unstick(PointI target)
    {
        if (ActiveClip() is { } clip)
        {
            NudgeInside(clip, 0);
            Thread.Sleep(20);
            NudgeInside(clip, 1);
        }
        else
        {
            NudgeRelative(NudgeStep, 0);
            Thread.Sleep(20);
            NudgeRelative(-NudgeStep, 0);
        }

        Thread.Sleep(20);
        AbsoluteSend(target);
    }

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

        // Already pinned earlier in this command: one cheap unstick attempt, then answer
        // honestly and immediately. Re-paying the clip budget for every remaining point is
        // what turned one pinned pointer into 27 x 500 ms of waiting on this machine.
        if (pinned)
        {
            Unstick(target);
            var thawed = WaitStable();

            if (MaxDistance(thawed, target) <= tolerance)
            {
                ClearFreeze();
                return new MoveReport(target, thawed, 1, "sendinput+after-freeze");
            }

            return new MoveReport(target, thawed, 1, "frozen") { Frozen = true };
        }

        // A clip can be already in force when the move starts; it is often released a
        // moment later, so give it a short chance before declaring the point unreachable.
        var clipWaitMs = 0;
        if (ActiveClip() is { } pending && !pending.Contains(target))
        {
            var released = WaitForClipRelease(target, ClipWaitBudgetMs);
            if (released < 0)
            {
                var stuck = Get();
                var stillPinned = RegisterClipFailure(target, stuck);
                return new MoveReport(target, stuck, 1, "clipped")
                {
                    ClipWaitMs = ClipWaitBudgetMs,
                    Frozen = stillPinned,
                };
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
                ClearFreeze();
                return new MoveReport(target, landed, attempts, strategy.Name) { ClipWaitMs = clipWaitMs };
            }

            // A cursor that is clipped to a rectangle excluding the target cannot be moved
            // there by any strategy — but the clip is frequently transient, so wait for it
            // instead of burning the walk and route attempts (and say how long we waited).
            if (ActiveClip() is { } clip && !clip.Contains(target))
            {
                var released = WaitForClipRelease(target, ClipWaitBudgetMs);
                if (released < 0)
                {
                    var stillPinned = RegisterClipFailure(target, landed);
                    return new MoveReport(target, landed, attempts, "clipped")
                    {
                        ClipWaitMs = clipWaitMs + ClipWaitBudgetMs,
                        Frozen = stillPinned,
                    };
                }

                clipWaitMs += released;

                // The clip was the only reason this attempt failed: try it again on its own
                // terms rather than escalating to a strategy that moves the cursor differently.
                strategy.Apply(target);
                landed = WaitStable();

                if (MaxDistance(landed, target) <= tolerance)
                {
                    ClearFreeze();
                    return new MoveReport(target, landed, attempts, strategy.Name + "+after-clip")
                    {
                        ClipWaitMs = clipWaitMs,
                    };
                }
            }
        }

        var pinnedNow = RegisterClipFailure(target, landed);
        return new MoveReport(target, landed, attempts, pinnedNow ? "frozen" : "failed")
        {
            ClipWaitMs = clipWaitMs,
            Frozen = pinnedNow,
        };
    }

    /// <summary>Moves or throws with a classified diagnosis.</summary>
    public static MoveReport Require(PointI target, int tolerance, string action)
    {
        var report = Move(target, tolerance);
        if (MaxDistance(report.Landed, target) > tolerance)
        {
            var hints = Diagnose(report.Requested, report.Landed, out var causes);

            // Raise this whenever the failure is a confinement that outlived the wait and the
            // nudges: that is a pinned pointer whether or not three points had time to prove it.
            // (Before this, a single click could never reach this branch, so the error code was
            // documented but unreachable.)
            if (report.Frozen || report.Strategy is "clipped" or "frozen")
            {
                // A confinement is the common cause, but not the only one: the freeze is
                // "three different targets all refused", which is also what a pointer that
                // simply stopped answering looks like. Say which case was observed.
                var clip = ActiveClip();
                var reason = clip is { } confinement
                    ? $"the cursor is confined to {confinement.X},{confinement.Y} {confinement.W}x{confinement.H}, "
                      + "which excludes that point, and moving the pointer did not lift the confinement"
                    : "the pointer stopped moving although different points were requested, and no confinement explains it";

                throw new CliError(
                    "PointerFrozen",
                    $"{action}: the pointer is pinned at {report.Landed} -- {reason}, so further attempts "
                    + "would only repeat it.",
                    context: new JsonObject
                    {
                        ["requested"] = Out.Point(report.Requested),
                        ["landed"] = Out.Point(report.Landed),
                        ["delta"] = report.Delta,
                        ["attempts"] = report.Attempts,
                        ["strategy"] = report.Strategy,
                        ["landedMonitor"] = Monitors.At(report.Landed)?.Index,
                        ["pinnedAt"] = Out.Point(report.Landed),
                        ["clip"] = clip is { } rect ? Out.Rect(rect) : null,
                        ["causes"] = Out.Strings(causes),
                    },
                    hints: clip is not null
                        ?
                        [
                            "a mouse manager or remote-desktop host is holding the pointer: the confinement it sets excludes the target",
                            "small relative moves often release it; this command already tried that",
                            "move the pointer by hand once (or restart the mouse manager) and retry",
                        ]
                        :
                        [
                            "something is eating pointer input without leaving a confinement behind",
                            "check whether an input hook or a protected application owns the pointer, then retry",
                        ]);
            }

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

    /// <summary>
    /// Waits until the clip stops excluding the target; -1 when it never did.
    /// Waiting on its own does not lift a clip that an input hook is holding: measured on this
    /// machine with LittleBigMouse running, the pointer stayed confined to DISPLAY1 for 40 s of
    /// complete input silence, while the first 40 px relative move released the clip within
    /// ~120 ms. So move, and keep the displacements small and alternating so the net travel is
    /// about zero; the absolute move that follows decides where the pointer ends up.
    /// </summary>
    private static int WaitForClipRelease(PointI target, int budgetMs)
    {
        var watch = Stopwatch.StartNew();
        var nudges = 0;

        while (watch.ElapsedMilliseconds < budgetMs)
        {
            Thread.Sleep(ClipPollMs);

            if (ActiveClip() is not { } clip || clip.Contains(target))
            {
                return (int)watch.ElapsedMilliseconds;
            }

            if (nudges < NudgeMax)
            {
                NudgeInside(clip, nudges);
                nudges++;
            }
        }

        return -1;
    }

    /// <summary>
    /// A relative move that the clip cannot swallow: toward the middle of the confinement, so
    /// the pointer actually travels. Nudging along a clipped axis (the first version pushed
    /// horizontally at a pointer pinned on a left or right edge) moves nothing and releases
    /// nothing — measured on this machine, a pointer pinned on DISPLAY4's right edge kept its
    /// clip for the whole 500 ms.
    /// </summary>
    private static void NudgeInside(RectI clip, int index)
    {
        var current = Get();
        var dx = Math.Sign(clip.CenterX - current.X) * NudgeStep;
        var dy = Math.Sign(clip.CenterY - current.Y) * NudgeStep;

        // Alternate the axis so the pointer moves in both directions, not along one line.
        var moveX = index % 2 == 0 ? dx : 0;
        var moveY = index % 2 == 0 ? 0 : dy;

        if (moveX == 0 && moveY == 0)
        {
            moveX = index % 4 < 2 ? NudgeStep : -NudgeStep;
        }

        NudgeRelative(moveX, moveY);
    }

    /// <summary>One relative move: motion, not a teleport. Hooks that track movement reset on it.</summary>
    private static void NudgeRelative(int dx, int dy)
    {
        NativeInput.Send(NativeInput.MouseEvent(NativeInput.MouseMove, dx, dy));
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

    /// <summary>
    /// Crosses the monitors the pointer actually has to cross, one shared edge at a time.
    /// The previous version hopped from one monitor centre to the other, which for panels that
    /// are not adjacent (DISPLAY2 to DISPLAY4 on this desk) means teleporting over empty desktop
    /// and hoping the retry ladder survives it. Hop counts and seam geometry come from
    /// <see cref="Geo.Seams"/>, so the path is planned rather than guessed.
    /// </summary>
    private static void RouteTo(PointI target)
    {
        var from = Get();
        var start = Monitors.At(from);
        var goal = Monitors.At(target);

        if (start is null || goal is null || start.Index == goal.Index)
        {
            WalkTo(target);
            return;
        }

        var monitors = Monitors.All();
        var path = Seams.Path(monitors, start.Index, goal.Index);

        if (path.Count < 2)
        {
            // No shared edges at all: nothing better than approaching the point directly.
            WalkTo(target);
            return;
        }

        _ = WalkRoute(from, target);
        WalkTo(target);
    }

    /// <summary>One planned crossing: which seam, where it was aimed and where the pointer ended.</summary>
    internal sealed record RouteHop(string Kind, int From, int To, PointI Approach, PointI Requested, PointI Landed, int Delta);

    /// <summary>
    /// Walks the monitor adjacency path from one point to another, one shared edge at a time.
    /// Used by <see cref="RouteTo"/> and by the probe, so what the probe measures is the routing
    /// the tool actually performs.
    /// </summary>
    internal static (bool Ok, string Path, List<RouteHop> Hops) WalkRoute(PointI start, PointI target)
    {
        var hops = new List<RouteHop>();
        var startMonitor = Monitors.At(start);
        var goalMonitor = Monitors.At(target);

        if (startMonitor is null || goalMonitor is null || startMonitor.Index == goalMonitor.Index)
        {
            return (false, string.Empty, hops);
        }

        var monitors = Monitors.All();
        var path = Seams.Path(monitors, startMonitor.Index, goalMonitor.Index);

        if (path.Count < 2)
        {
            return (false, string.Join(">", path), hops);
        }

        var here = start;
        var ok = true;

        for (var hop = 0; hop + 1 < path.Count; hop++)
        {
            var seam = Seams.Find(monitors, path[hop], path[hop + 1]);
            if (seam is null)
            {
                ok = false;
                break;
            }

            // Cross at the stretch of the seam nearest to where the pointer already is.
            var along = seam.Along(seam.IsVertical ? here.Y : here.X);
            var (approach, land, _) = seam.Points(SeamStep, along);

            AbsoluteSend(approach);
            Thread.Sleep(5);

            var landed = Move(land, 1);
            hops.Add(new RouteHop(
                seam.IsVertical ? "vertical" : "horizontal",
                path[hop],
                path[hop + 1],
                approach,
                land,
                landed.Landed,
                landed.Delta));

            if (MaxDistance(landed.Landed, land) > 2)
            {
                ok = false;
            }

            here = landed.Landed;
        }

        return (ok, string.Join(">", path), hops);
    }
}
