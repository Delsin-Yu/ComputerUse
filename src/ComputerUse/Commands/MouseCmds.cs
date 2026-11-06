using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Input;

namespace ComputerUse.Commands;

internal static class MouseCmds
{
    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        "--window", "--index", "--at", "--from", "--to", "--button", "--clicks",
        "--tolerance", "--steps", "--zoom", "--delta",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--shot" };

    public static int Run(string[] argv, int start, string verb)
    {
        Dispatch.LastCommand = "mouse " + verb;

        return verb switch
        {
            "move" => Move(argv, start),
            "click" => Click(argv, start),
            "drag" => Drag(argv, start),
            "scroll" => Scroll(argv, start),
            "down" => Button(argv, start, down: true),
            "up" => Button(argv, start, down: false),
            _ => throw new UsageException($"Unknown mouse verb '{verb}'.", Usage.Mouse),
        };
    }

    private static int Move(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "mouse move", Usage.Mouse, Values, Flags);
        var space = Targets.ForInput(args);
        var tolerance = Targets.Tolerance(args);
        var point = space.ToScreen(args.RequirePoint("--at"));

        var report = Pointer.Require(point, tolerance, "move");

        var response = new Response("mouse move");
        response.Root["target"] = SpaceTarget(space);
        response.Root["input"] = Targets.InputReport(report, space);
        response.Pointer(Pointer.Get());

        if (args.Flag("--shot"))
        {
            Shoot(response, space, point, report, args.IntOr("--zoom", 4), "move");
        }

        return response.Emit();
    }

    private static int Click(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "mouse click", Usage.Mouse, Values, Flags);
        var space = Targets.ForInput(args);
        var tolerance = Targets.Tolerance(args);
        var point = space.ToScreen(args.RequirePoint("--at"));
        var button = Mouse.ParseButton(args.Get("--button"));
        var clicks = args.IntOr("--clicks", 1);

        var report = Mouse.Click(point, button, clicks, tolerance, 50);

        var response = new Response("mouse click");
        response.Root["target"] = SpaceTarget(space);
        response.Root["input"] = Targets.InputReport(
            report,
            space,
            extra: new JsonObject
            {
                ["button"] = button.ToString().ToLowerInvariant(),
                ["clicks"] = clicks,
            });

        var pointerAfter = Pointer.Get();
        response.Pointer(pointerAfter);

        if (pointerAfter != report.Landed)
        {
            response.Warn("pointer-moved-during-click: something positioned the pointer while the button was pressed");
        }

        if (args.Flag("--shot"))
        {
            Shoot(response, space, point, report, args.IntOr("--zoom", 4), "click");
        }

        return response.Emit();
    }

    private static int Drag(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "mouse drag", Usage.Mouse, Values, Flags);
        var space = Targets.ForInput(args);
        var tolerance = Targets.Tolerance(args);

        var from = space.ToScreen(args.RequirePoint("--from"));
        var to = space.ToScreen(args.RequirePoint("--to"));
        var steps = Math.Clamp(args.IntOr("--steps", 20), 1, 120);

        var startReport = Pointer.Require(from, tolerance, "drag start");

        Mouse.Down(MouseButton.Left);
        try
        {
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (double)steps;
                var step = new PointI(
                    (int)Math.Round(from.X + ((to.X - from.X) * t)),
                    (int)Math.Round(from.Y + ((to.Y - from.Y) * t)));

                Pointer.Jump(step);
                Thread.Sleep(3);
            }

            Pointer.Require(to, tolerance, "drag end");
        }
        finally
        {
            Mouse.Up(MouseButton.Left);
        }

        var landed = Pointer.WaitStable();

        var response = new Response("mouse drag");
        response.Root["target"] = SpaceTarget(space);
        response.Root["input"] = new JsonObject
        {
            ["from"] = new JsonObject
            {
                ["requested"] = Out.Point(space.ToSpace(startReport.Requested)),
                ["landed"] = Out.Point(space.ToSpace(startReport.Landed)),
                ["landedScreen"] = Out.Point(startReport.Landed),
            },
            ["to"] = new JsonObject
            {
                ["requested"] = Out.Point(space.ToSpace(to)),
                ["landed"] = Out.Point(space.ToSpace(landed)),
                ["landedScreen"] = Out.Point(landed),
            },
            ["steps"] = steps,
            ["delta"] = Pointer.MaxDistance(landed, to),
            ["strategy"] = startReport.Strategy,
            ["pointerAfter"] = Out.Point(landed),
        };

        response.Pointer(landed);

        if (args.Flag("--shot"))
        {
            Shoot(response, space, to, new MoveReport(to, landed, 1, "drag"), args.IntOr("--zoom", 4), "drag");
        }

        return response.Emit();
    }

    private static int Scroll(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "mouse scroll", Usage.Mouse, Values, Flags);
        var notches = args.RequireInt("--delta");
        if (notches is < -60 or > 60)
        {
            throw new UsageException("--delta must be between -60 and 60 wheel notches.", Usage.Mouse);
        }

        var tolerance = Targets.Tolerance(args);

        Space? space = null;
        MoveReport? report = null;

        if (args.Point("--at") is { } at)
        {
            space = Targets.ForInput(args);
            var point = space.ToScreen(at);
            report = Pointer.Require(point, tolerance, "scroll");
        }

        var wheelUnits = notches * 120;
        Mouse.Wheel(wheelUnits);

        var response = new Response("mouse scroll");
        if (space is not null)
        {
            response.Root["target"] = SpaceTarget(space);
        }

        if (space is not null && report is not null)
        {
            response.Root["input"] = Targets.InputReport(report, space, extra: new JsonObject { ["notches"] = notches, ["wheelUnits"] = wheelUnits });
        }
        else
        {
            response.Root["input"] = new JsonObject
            {
                ["notches"] = notches,
                ["wheelUnits"] = wheelUnits,
                ["pointerAfter"] = Out.Point(Pointer.Get()),
            };
        }

        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static int Button(string[] argv, int start, bool down)
    {
        var verb = down ? "mouse down" : "mouse up";
        var args = Args.Parse(argv, start, verb, Usage.Mouse, Values, Flags);
        var button = Mouse.ParseButton(args.Get("--button"));
        var tolerance = Targets.Tolerance(args);

        Space? space = null;
        MoveReport? report = null;

        if (down && args.Point("--at") is { } at)
        {
            space = Targets.ForInput(args);
            report = Pointer.Require(space.ToScreen(at), tolerance, verb);
        }

        if (down)
        {
            Mouse.Down(button);
        }
        else
        {
            Mouse.Up(button);
        }

        var response = new Response(verb);
        if (space is not null)
        {
            response.Root["target"] = SpaceTarget(space);
        }

        if (space is not null && report is not null)
        {
            response.Root["input"] = Targets.InputReport(
                report,
                space,
                extra: new JsonObject { ["button"] = button.ToString().ToLowerInvariant() });
        }

        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static JsonObject SpaceTarget(Space space)
    {
        if (space.Window is { } window)
        {
            return Targets.WindowTarget(window);
        }

        var pointer = Pointer.Get();
        return new JsonObject
        {
            ["kind"] = "desktop",
            ["space"] = "desktop",
            ["monitor"] = Monitors.At(pointer)?.Index,
        };
    }

    private static void Shoot(Response response, Space space, PointI requestedScreen, MoveReport report, int zoom, string kind)
    {
        // MoveReport carries screen (desktop) pixels: Pointer never works in a window's space,
        // only Targets.InputReport translates for the JSON. Converting again here offset the
        // confirmation crop by the window origin — measured: a window at x=1947 aimed at
        // window 461 produced a crop centred on 4355 instead of 2408.
        var landedScreen = report.Landed;
        var (image, geometry) = Shots.Around(landedScreen, requestedScreen, landedScreen, zoom, kind);
        response.Root["image"] = image;
        response.Root["geometry"] = geometry;
    }
}
