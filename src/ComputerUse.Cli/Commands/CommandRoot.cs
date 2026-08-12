using System.CommandLine;
using ComputerUse.Cli.Json;
using ComputerUse.Cli.Uia;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Commands;

internal static class CommandRoot
{
    public static RootCommand Build()
    {
        var root = new RootCommand("Stateless Windows computer-use CLI for agents.");
        root.Subcommands.Add(BuildCursor());
        root.Subcommands.Add(BuildMouse());
        root.Subcommands.Add(BuildKey());
        root.Subcommands.Add(BuildScreenshot());
        root.Subcommands.Add(BuildScreen());
        root.Subcommands.Add(BuildWindow());
        root.Subcommands.Add(BuildUia());
        return root;
    }

    private static Command BuildCursor()
    {
        var cursor = new Command("cursor", "Cursor position commands.");

        var get = new Command("get", "Report the current cursor position and virtual screen bounds.");
        get.SetAction(_ => Run("cursor get", b =>
        {
            var pos = CursorService.GetPosition();
            b.WithCursor(new PointDto { X = pos.X, Y = pos.Y })
                .WithScreen(ScreenService.GetInfo());
        }));

        var xOption = RequiredInt("--x", "Absolute X coordinate.");
        var yOption = RequiredInt("--y", "Absolute Y coordinate.");
        var move = new Command("move", "Move the cursor to an absolute screen position.")
        {
            xOption,
            yOption,
        };
        move.SetAction(parse => Run("cursor move", _ =>
        {
            CursorService.Move(parse.GetValue(xOption), parse.GetValue(yOption));
        }));

        cursor.Subcommands.Add(get);
        cursor.Subcommands.Add(move);
        return cursor;
    }

    private static Command BuildMouse()
    {
        var mouse = new Command("mouse", "Mouse input commands.");

        var buttonOption = new Option<string>("--button")
        {
            Description = "Mouse button: left, right, or middle.",
            DefaultValueFactory = _ => "left",
        };
        var xOption = OptionalInt("--x", "Optional absolute X before the action.");
        var yOption = OptionalInt("--y", "Optional absolute Y before the action.");
        var clicksOption = new Option<int>("--clicks")
        {
            Description = "Number of clicks (1-10).",
            DefaultValueFactory = _ => 1,
        };
        var clickDelayOption = new Option<int>("--click-delay-ms")
        {
            Description = "Delay between multi-clicks in milliseconds.",
            DefaultValueFactory = _ => 50,
        };
        var hwndOption = new Option<string?>("--hwnd")
        {
            Description = "Window handle when clicking a snapshot --ref.",
        };
        var refOption = new Option<string?>("--ref")
        {
            Description = "Snapshot ref from 'window snapshot' (e.g. e12). Requires --hwnd.",
        };

        var click = new Command("click", "Click a mouse button (coords or --hwnd/--ref).")
        {
            buttonOption,
            xOption,
            yOption,
            clicksOption,
            clickDelayOption,
            hwndOption,
            refOption,
        };
        click.SetAction(parse => Run("mouse click", b =>
        {
            var refText = parse.GetValue(refOption);
            var hwndText = parse.GetValue(hwndOption);
            var x = parse.GetValue(xOption);
            var y = parse.GetValue(yOption);
            var button = InputService.ParseButton(parse.GetValue(buttonOption));
            var clicks = parse.GetValue(clicksOption);
            var clickDelay = parse.GetValue(clickDelayOption);

            if (!string.IsNullOrWhiteSpace(refText))
            {
                if (string.IsNullOrWhiteSpace(hwndText))
                {
                    throw new CliException("InvalidArgument", "--ref requires --hwnd.");
                }

                if (x is not null || y is not null)
                {
                    throw new CliException("InvalidArgument", "Use either --ref or --x/--y, not both.");
                }

                var hwnd = HwndFormat.Parse(hwndText);
                var element = UiaActionService.ClickRef(hwnd, refText, button, clicks);
                b.WithElement(element).WithWindows([WindowService.Get(hwnd)]);
                return;
            }

            InputService.Click(button, x, y, clicks, clickDelay);
        }));

        var down = new Command("down", "Press and hold a mouse button.")
        {
            buttonOption,
            xOption,
            yOption,
        };
        down.SetAction(parse => Run("mouse down", _ =>
        {
            InputService.ButtonDown(
                InputService.ParseButton(parse.GetValue(buttonOption)),
                parse.GetValue(xOption),
                parse.GetValue(yOption));
        }));

        var up = new Command("up", "Release a mouse button.")
        {
            buttonOption,
            xOption,
            yOption,
        };
        up.SetAction(parse => Run("mouse up", _ =>
        {
            InputService.ButtonUp(
                InputService.ParseButton(parse.GetValue(buttonOption)),
                parse.GetValue(xOption),
                parse.GetValue(yOption));
        }));

        var deltaOption = RequiredInt("--delta", "Vertical wheel delta (positive = away from user).");
        var dxOption = OptionalInt("--dx", "Optional horizontal wheel delta.");
        var scrollX = OptionalInt("--x", "Optional absolute X before scrolling.");
        var scrollY = OptionalInt("--y", "Optional absolute Y before scrolling.");
        var scroll = new Command("scroll", "Scroll the mouse wheel.")
        {
            deltaOption,
            dxOption,
            scrollX,
            scrollY,
        };
        scroll.SetAction(parse => Run("mouse scroll", _ =>
        {
            InputService.Scroll(
                parse.GetValue(deltaOption),
                parse.GetValue(dxOption),
                parse.GetValue(scrollX),
                parse.GetValue(scrollY));
        }));

        var fromX = RequiredInt("--from-x", "Drag start X.");
        var fromY = RequiredInt("--from-y", "Drag start Y.");
        var toX = RequiredInt("--to-x", "Drag end X.");
        var toY = RequiredInt("--to-y", "Drag end Y.");
        var stepsOption = new Option<int>("--steps")
        {
            Description = "Intermediate drag steps (smoother UI tracking).",
            DefaultValueFactory = _ => 12,
        };
        var stepDelayOption = new Option<int>("--step-delay-ms")
        {
            Description = "Delay between drag steps in milliseconds.",
            DefaultValueFactory = _ => 8,
        };
        var drag = new Command("drag", "Drag with a mouse button held.")
        {
            fromX,
            fromY,
            toX,
            toY,
            buttonOption,
            stepsOption,
            stepDelayOption,
        };
        drag.SetAction(parse => Run("mouse drag", _ =>
        {
            InputService.Drag(
                parse.GetValue(fromX),
                parse.GetValue(fromY),
                parse.GetValue(toX),
                parse.GetValue(toY),
                InputService.ParseButton(parse.GetValue(buttonOption)),
                parse.GetValue(stepsOption),
                parse.GetValue(stepDelayOption));
        }));

        var hoverX = RequiredInt("--x", "Absolute X.");
        var hoverY = RequiredInt("--y", "Absolute Y.");
        var hover = new Command("hover", "Move the cursor without clicking.")
        {
            hoverX,
            hoverY,
        };
        hover.SetAction(parse => Run("mouse hover", _ =>
        {
            InputService.MoveAbsolutePublic(parse.GetValue(hoverX), parse.GetValue(hoverY));
        }));

        mouse.Subcommands.Add(click);
        mouse.Subcommands.Add(down);
        mouse.Subcommands.Add(up);
        mouse.Subcommands.Add(scroll);
        mouse.Subcommands.Add(drag);
        mouse.Subcommands.Add(hover);
        return mouse;
    }

    private static Command BuildKey()
    {
        var key = new Command("key", "Keyboard input commands.");

        var textOption = new Option<string>("--text")
        {
            Description = "Unicode text to type.",
            Required = true,
        };
        var delayOption = new Option<int>("--delay-ms")
        {
            Description = "Optional delay between characters in milliseconds.",
            DefaultValueFactory = _ => 0,
        };
        var type = new Command("type", "Type Unicode text.")
        {
            textOption,
            delayOption,
        };
        type.SetAction(parse => Run("key type", _ =>
        {
            InputService.TypeText(parse.GetValue(textOption) ?? string.Empty, parse.GetValue(delayOption));
        }));

        var keyOption = new Option<string>("--key")
        {
            Description = "Key name (enter, ctrl, a, F5, VK_0x1B, ...).",
            Required = true,
        };
        var modifiersOption = new Option<string?>("--modifiers")
        {
            Description = "Comma-separated modifiers (ctrl,shift,alt,win).",
        };

        var tap = new Command("tap", "Press and release a key, optionally with modifiers.")
        {
            keyOption,
            modifiersOption,
        };
        tap.SetAction(parse => Run("key tap", _ =>
        {
            var vk = KeyMap.Parse(parse.GetValue(keyOption) ?? string.Empty);
            var mods = KeyMap.ParseModifiers(parse.GetValue(modifiersOption));
            InputService.TapKey(vk, mods);
        }));

        var down = new Command("down", "Press and hold a key.")
        {
            keyOption,
        };
        down.SetAction(parse => Run("key down", _ =>
        {
            InputService.KeyDown(KeyMap.Parse(parse.GetValue(keyOption) ?? string.Empty));
        }));

        var up = new Command("up", "Release a key.")
        {
            keyOption,
        };
        up.SetAction(parse => Run("key up", _ =>
        {
            InputService.KeyUp(KeyMap.Parse(parse.GetValue(keyOption) ?? string.Empty));
        }));

        key.Subcommands.Add(type);
        key.Subcommands.Add(tap);
        key.Subcommands.Add(down);
        key.Subcommands.Add(up);
        return key;
    }

    private static Command BuildScreenshot()
    {
        var pathOption = new Option<string>("--path")
        {
            Description = "Output file path, or a directory (ending with \\ or /) for an auto-generated PNG name.",
            Required = true,
        };
        var xOption = OptionalInt("--x", "Capture region X (absolute, or relative to monitor when --monitor is set).");
        var yOption = OptionalInt("--y", "Capture region Y.");
        var widthOption = OptionalInt("--width", "Capture region width.");
        var heightOption = OptionalInt("--height", "Capture region height.");
        var monitorOption = OptionalInt("--monitor", "Monitor index from 'screen info'.");
        var hwndOption = new Option<string?>("--hwnd")
        {
            Description = "Capture this window (outer bounds). Combine with --ref for an element crop, or --annotate for SoM.",
        };
        var refOption = new Option<string?>("--ref")
        {
            Description = "Snapshot ref to crop (e.g. e14). Requires --hwnd.",
        };
        var padOption = new Option<int>("--pad")
        {
            Description = "Pixels to expand around a --ref crop (default 8).",
            DefaultValueFactory = _ => 8,
        };
        var aroundXOption = OptionalInt("--around-x", "Center X for a square POI crop (requires --around-y and --size).");
        var aroundYOption = OptionalInt("--around-y", "Center Y for a square POI crop (requires --around-x and --size).");
        var sizeOption = OptionalInt("--size", "Square size for --around-x/--around-y POI crop.");
        var annotateOption = new Option<bool>("--annotate")
        {
            Description = "Overlay ARIA snapshot refs on a full-window capture (requires --hwnd).",
            DefaultValueFactory = _ => false,
        };

        var screenshot = new Command("screenshot", "Capture the screen, a window, or a POI region to a PNG file.")
        {
            pathOption,
            xOption,
            yOption,
            widthOption,
            heightOption,
            monitorOption,
            hwndOption,
            refOption,
            padOption,
            aroundXOption,
            aroundYOption,
            sizeOption,
            annotateOption,
        };

        screenshot.SetAction(parse => Run("screenshot", b =>
        {
            var path = parse.GetValue(pathOption) ?? string.Empty;
            var annotate = parse.GetValue(annotateOption);
            var hwndText = parse.GetValue(hwndOption);
            var refText = parse.GetValue(refOption);
            var aroundX = parse.GetValue(aroundXOption);
            var aroundY = parse.GetValue(aroundYOption);
            var size = parse.GetValue(sizeOption);
            var x = parse.GetValue(xOption);
            var y = parse.GetValue(yOption);
            var width = parse.GetValue(widthOption);
            var height = parse.GetValue(heightOption);
            var monitor = parse.GetValue(monitorOption);

            var hasAround = aroundX is not null || aroundY is not null || size is not null;
            var hasAbsolute = x is not null || y is not null || width is not null || height is not null || monitor is not null;
            var hasHwnd = !string.IsNullOrWhiteSpace(hwndText);
            var hasRef = !string.IsNullOrWhiteSpace(refText);

            // Default full virtual desktop is allowed when nothing targeting is set.
            if ((hasHwnd || hasRef || annotate || hasAround) && hasAbsolute)
            {
                throw new CliException(
                    "InvalidArgument",
                    "Do not mix absolute --x/--y/--width/--height/--monitor with --hwnd/--ref/--around-*.");
            }

            if (hasAround && (hasHwnd || hasRef || annotate))
            {
                throw new CliException(
                    "InvalidArgument",
                    "Do not mix --around-x/--around-y/--size with --hwnd/--ref/--annotate.");
            }

            if (annotate)
            {
                if (!hasHwnd)
                {
                    throw new CliException("InvalidArgument", "--annotate requires --hwnd.");
                }

                if (hasRef)
                {
                    throw new CliException("InvalidArgument", "--annotate captures the full window; do not pass --ref.");
                }

                var hwnd = HwndFormat.Parse(hwndText!);
                var snap = AriaSnapshotService.Snapshot(hwnd, new SnapshotOptions
                {
                    View = SnapshotView.Control,
                    InteractiveOnly = true,
                    Depth = 8,
                    MaxNodes = 500,
                    Format = SnapshotFormat.Both,
                });
                var annotated = SnapshotAnnotator.CaptureAnnotated(hwnd, snap, path);
                b.WithScreenshot(annotated)
                    .WithWindowSnapshot(snap)
                    .WithWindows([WindowService.Get(hwnd)]);
                return;
            }

            if (hasAround)
            {
                if (aroundX is null || aroundY is null || size is null)
                {
                    throw new CliException("InvalidArgument", "--around-x, --around-y, and --size are required together.");
                }

                var region = ScreenService.FromCenterSize(aroundX.Value, aroundY.Value, size.Value);
                b.WithScreenshot(ScreenshotService.CaptureRegionToPath(region, path));
                return;
            }

            if (hasRef)
            {
                if (!hasHwnd)
                {
                    throw new CliException("InvalidArgument", "--ref requires --hwnd.");
                }

                var hwnd = HwndFormat.Parse(hwndText!);
                using var resolved = UiaActionService.AcquireByRef(hwnd, refText!);
                var pad = parse.GetValue(padOption);
                var region = ScreenService.Inflate(resolved.Info.Bounds, pad);
                b.WithScreenshot(ScreenshotService.CaptureRegionToPath(region, path))
                    .WithElement(resolved.Info)
                    .WithWindows([WindowService.Get(hwnd)]);
                return;
            }

            if (hasHwnd)
            {
                var hwnd = HwndFormat.Parse(hwndText!);
                var region = ScreenService.FromHwnd(hwnd);
                b.WithScreenshot(ScreenshotService.CaptureRegionToPath(region, path))
                    .WithWindows([WindowService.Get(hwnd)]);
                return;
            }

            var result = ScreenshotService.Capture(path, x, y, width, height, monitor);
            b.WithScreenshot(result);
        }));

        return screenshot;
    }

    private static Command BuildScreen()
    {
        var screen = new Command("screen", "Screen / monitor information.");
        var info = new Command("info", "List virtual desktop bounds and monitors.");
        info.SetAction(_ => Run("screen info", b =>
        {
            b.WithScreen(ScreenService.GetInfo());
        }));
        screen.Subcommands.Add(info);
        return screen;
    }

    private static Command BuildWindow()
    {
        var window = new Command("window", "Top-level window discovery.");

        var includeEmptyTitle = new Option<bool>("--include-empty-title")
        {
            Description = "Include windows with an empty title.",
            DefaultValueFactory = _ => false,
        };
        var all = new Option<bool>("--all")
        {
            Description = "Include non-visible and minimized windows.",
            DefaultValueFactory = _ => false,
        };

        var list = new Command("list", "List top-level windows the agent can target.")
        {
            includeEmptyTitle,
            all,
        };
        list.SetAction(parse => Run("window list", b =>
        {
            b.WithWindows(WindowService.List(
                parse.GetValue(includeEmptyTitle),
                parse.GetValue(all)));
        }));

        var focusHwndOption = new Option<string>("--hwnd")
        {
            Description = "Target window handle (decimal or 0x-hex from 'window list').",
            Required = true,
        };
        var focus = new Command("focus", "Restore (if minimized) and bring a window to the foreground.")
        {
            focusHwndOption,
        };
        focus.SetAction(parse => Run("window focus", b =>
        {
            var hwnd = HwndFormat.Parse(parse.GetValue(focusHwndOption) ?? string.Empty);
            b.WithWindows(WindowService.Focus(hwnd));
        }));

        var snapshotHwndOption = new Option<string>("--hwnd")
        {
            Description = "Target window handle (decimal or 0x-hex from 'window list').",
            Required = true,
        };
        var viewOption = new Option<string>("--view")
        {
            Description = "UIA tree view: control (default), content, or raw.",
            DefaultValueFactory = _ => "control",
        };
        var interactiveOnlyOption = new Option<bool>("--interactive-only")
        {
            Description = "Keep actionable / named content nodes; prune empty layout chrome.",
            DefaultValueFactory = _ => false,
        };
        var depthOption = new Option<int>("--depth")
        {
            Description = "Maximum tree depth to expand.",
            DefaultValueFactory = _ => 8,
        };
        var maxNodesOption = new Option<int>("--max-nodes")
        {
            Description = "Maximum nodes to visit before truncation.",
            DefaultValueFactory = _ => 500,
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Payload shape: both (default), yaml, or json.",
            DefaultValueFactory = _ => "both",
        };
        var annotateOption = new Option<bool>("--annotate")
        {
            Description = "Also write a Set-of-Mark PNG with matching refs (requires --path).",
            DefaultValueFactory = _ => false,
        };
        var pathOption = new Option<string?>("--path")
        {
            Description = "Output path/dir for --annotate PNG.",
        };

        var snapshot = new Command("snapshot", "Agent-friendly ARIA snapshot (YAML + ref map) for a window.")
        {
            snapshotHwndOption,
            viewOption,
            interactiveOnlyOption,
            depthOption,
            maxNodesOption,
            formatOption,
            annotateOption,
            pathOption,
        };
        snapshot.SetAction(parse => Run("window snapshot", b =>
        {
            var hwnd = HwndFormat.Parse(parse.GetValue(snapshotHwndOption) ?? string.Empty);
            var options = new SnapshotOptions
            {
                View = ParseSnapshotView(parse.GetValue(viewOption)),
                InteractiveOnly = parse.GetValue(interactiveOnlyOption),
                Depth = parse.GetValue(depthOption),
                MaxNodes = parse.GetValue(maxNodesOption),
                Format = ParseSnapshotFormat(parse.GetValue(formatOption)),
            };
            var snap = AriaSnapshotService.Snapshot(hwnd, options);
            b.WithWindowSnapshot(snap)
                .WithWindows([WindowService.Get(hwnd)]);

            if (parse.GetValue(annotateOption))
            {
                var path = parse.GetValue(pathOption);
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new CliException("InvalidArgument", "--annotate requires --path.");
                }

                var forAnnotate = snap.Elements is not null
                    ? snap
                    : AriaSnapshotService.Snapshot(hwnd, new SnapshotOptions
                    {
                        View = options.View,
                        InteractiveOnly = options.InteractiveOnly,
                        Depth = options.Depth,
                        MaxNodes = options.MaxNodes,
                        Format = SnapshotFormat.Both,
                    });
                b.WithScreenshot(SnapshotAnnotator.CaptureAnnotated(hwnd, forAnnotate, path));
            }
        }));

        window.Subcommands.Add(list);
        window.Subcommands.Add(focus);
        window.Subcommands.Add(snapshot);
        return window;
    }

    private static Command BuildUia()
    {
        var uia = new Command("uia", "UI Automation element tree and ref actions.");

        var dumpHwndOption = new Option<string>("--hwnd")
        {
            Description = "Target window handle (decimal or 0x-hex from 'window list').",
            Required = true,
        };
        var depthOption = new Option<int>("--depth")
        {
            Description = "Maximum tree depth to expand.",
            DefaultValueFactory = _ => 8,
        };
        var maxNodesOption = new Option<int>("--max-nodes")
        {
            Description = "Maximum nodes to return before truncation.",
            DefaultValueFactory = _ => 500,
        };
        var visibleOnlyOption = new Option<bool>("--visible-only")
        {
            Description = "Use the control view (skip non-control chrome). Use --visible-only false for raw view.",
            DefaultValueFactory = _ => true,
        };

        var dump = new Command("dump", "Dump the accessibility tree for a window.")
        {
            dumpHwndOption,
            depthOption,
            maxNodesOption,
            visibleOnlyOption,
        };
        dump.SetAction(parse => Run("uia dump", b =>
        {
            var hwnd = HwndFormat.Parse(parse.GetValue(dumpHwndOption) ?? string.Empty);
            b.WithUia(UiaService.Dump(
                hwnd,
                parse.GetValue(depthOption),
                parse.GetValue(maxNodesOption),
                parse.GetValue(visibleOnlyOption)));
        }));

        var xOption = RequiredInt("--x", "Absolute screen X.");
        var yOption = RequiredInt("--y", "Absolute screen Y.");
        var fromPoint = new Command("from-point", "Resolve the UI Automation element under a screen point.")
        {
            xOption,
            yOption,
        };
        fromPoint.SetAction(parse => Run("uia from-point", b =>
        {
            b.WithElement(AriaSnapshotService.FromPoint(
                parse.GetValue(xOption),
                parse.GetValue(yOption)));
        }));

        uia.Subcommands.Add(dump);
        uia.Subcommands.Add(fromPoint);
        uia.Subcommands.Add(BuildUiaRefAction("invoke", "Invoke a control via InvokePattern (buttons, menu items).",
            (hwnd, refText) => UiaActionService.Invoke(hwnd, refText)));
        uia.Subcommands.Add(BuildUiaSetValue());
        uia.Subcommands.Add(BuildUiaRefAction("toggle", "Toggle a control via TogglePattern (checkboxes).",
            (hwnd, refText) => UiaActionService.Toggle(hwnd, refText)));
        uia.Subcommands.Add(BuildUiaRefAction("select", "Select via SelectionItemPattern (tabs, list items).",
            (hwnd, refText) => UiaActionService.Select(hwnd, refText)));
        uia.Subcommands.Add(BuildUiaRefAction("expand", "Expand via ExpandCollapsePattern.",
            (hwnd, refText) => UiaActionService.Expand(hwnd, refText)));
        uia.Subcommands.Add(BuildUiaRefAction("collapse", "Collapse via ExpandCollapsePattern.",
            (hwnd, refText) => UiaActionService.Collapse(hwnd, refText)));
        uia.Subcommands.Add(BuildUiaRefAction("focus", "Focus an element via UI Automation SetFocus.",
            (hwnd, refText) => UiaActionService.Focus(hwnd, refText)));
        return uia;
    }

    private static Command BuildUiaSetValue()
    {
        var hwndOption = new Option<string>("--hwnd")
        {
            Description = "Target window handle from 'window list' / snapshot.",
            Required = true,
        };
        var refOption = new Option<string>("--ref")
        {
            Description = "Snapshot ref (e.g. e3).",
            Required = true,
        };
        var valueOption = new Option<string>("--value")
        {
            Description = "Value to set via ValuePattern.",
            Required = true,
        };
        var command = new Command("set-value", "Set a control value via ValuePattern (preferred over key type for edits).")
        {
            hwndOption,
            refOption,
            valueOption,
        };
        command.SetAction(parse => Run("uia set-value", b =>
        {
            var hwnd = HwndFormat.Parse(parse.GetValue(hwndOption) ?? string.Empty);
            var element = UiaActionService.SetValue(
                hwnd,
                parse.GetValue(refOption) ?? string.Empty,
                parse.GetValue(valueOption) ?? string.Empty);
            b.WithElement(element).WithWindows([WindowService.Get(hwnd)]);
        }));
        return command;
    }

    private static Command BuildUiaRefAction(string name, string description, Func<nint, string, SnapshotElementDto> action)
    {
        var hwndOption = new Option<string>("--hwnd")
        {
            Description = "Target window handle from 'window list' / snapshot.",
            Required = true,
        };
        var refOption = new Option<string>("--ref")
        {
            Description = "Snapshot ref (e.g. e12).",
            Required = true,
        };
        var command = new Command(name, description)
        {
            hwndOption,
            refOption,
        };
        command.SetAction(parse => Run($"uia {name}", b =>
        {
            var hwnd = HwndFormat.Parse(parse.GetValue(hwndOption) ?? string.Empty);
            var element = action(hwnd, parse.GetValue(refOption) ?? string.Empty);
            b.WithElement(element).WithWindows([WindowService.Get(hwnd)]);
        }));
        return command;
    }

    private static SnapshotView ParseSnapshotView(string? value) => (value ?? "control").Trim().ToLowerInvariant() switch
    {
        "control" => SnapshotView.Control,
        "content" => SnapshotView.Content,
        "raw" => SnapshotView.Raw,
        _ => throw new CliException("InvalidArgument", "--view must be control, content, or raw."),
    };

    private static SnapshotFormat ParseSnapshotFormat(string? value) => (value ?? "both").Trim().ToLowerInvariant() switch
    {
        "both" => SnapshotFormat.Both,
        "yaml" => SnapshotFormat.Yaml,
        "json" => SnapshotFormat.Json,
        _ => throw new CliException("InvalidArgument", "--format must be both, yaml, or json."),
    };

    private static Option<int> RequiredInt(string name, string description) => new(name)
    {
        Description = description,
        Required = true,
    };

    private static Option<int?> OptionalInt(string name, string description) => new(name)
    {
        Description = description,
    };

    private static int Run(string command, Action<ResponseWriter.SuccessBuilder> action)
    {
        try
        {
            return ResponseWriter.Success(command, action);
        }
        catch (Exception ex)
        {
            return ResponseWriter.Fail(command, ex);
        }
    }
}
