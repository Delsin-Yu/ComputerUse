using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Input;

namespace ComputerUse.Commands;

internal static class WindowCmds
{
    private static readonly HashSet<string> ListValues = new(StringComparer.Ordinal);

    private static readonly HashSet<string> ListFlags = new(StringComparer.Ordinal) { "--all", "--empty-title" };

    private static readonly HashSet<string> FindValues = new(StringComparer.Ordinal)
    {
        "--title", "--process", "--class", "--hwnd", "--index",
    };

    private static readonly HashSet<string> FindFlags = new(StringComparer.Ordinal) { "--all", "--empty-title" };

    private static readonly HashSet<string> FocusValues = new(StringComparer.Ordinal) { "--hwnd", "--window" };

    private static readonly HashSet<string> FocusFlags = new(StringComparer.Ordinal);

    private static readonly HashSet<string> WaitValues = new(StringComparer.Ordinal)
    {
        "--hwnd", "--window", "--index", "--title", "--process", "--class", "--timeout", "--poll-ms",
    };

    private static readonly HashSet<string> WaitFlags = new(StringComparer.Ordinal)
    {
        "--appear", "--disappear", "--all", "--empty-title",
    };

    private static readonly HashSet<string> PointValues = new(StringComparer.Ordinal) { "--at", "--window", "--index" };

    private static readonly HashSet<string> PointFlags = new(StringComparer.Ordinal);

    public static int Run(string[] argv, int start, string verb)
    {
        Dispatch.LastCommand = "window " + verb;

        return verb switch
        {
            "list" => List(argv, start),
            "find" => Find(argv, start),
            "move-to-foreground" => MoveToForeground(argv, start),
            "wait" => Wait(argv, start),
            "point-info" => PointInfo(argv, start),
            _ => throw new UsageException($"Unknown window verb '{verb}'.", Usage.Window),
        };
    }

    private static int List(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "window list", Usage.Window, ListValues, ListFlags);
        var windows = WindowList.List(args.Flag("--all"), args.Flag("--empty-title"));

        var response = new Response("window list");
        response.Root["count"] = windows.Count;
        response.Root["windows"] = ToArray(windows);
        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static int Find(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "window find", Usage.Window, FindValues, FindFlags);

        var includeInvisible = args.Flag("--all");
        var includeEmptyTitle = args.Flag("--empty-title");
        var index = args.Int("--index");

        if (args.Get("--hwnd") is { } hwndText)
        {
            var window = WindowList.Get(WindowList.ParseHwnd(hwndText));
            var single = new Response("window find");
            single.Root["windows"] = ToArray([window]);
            single.Pointer(Pointer.Get());
            return single.Emit();
        }

        var title = args.Get("--title");
        var process = args.Get("--process");
        var className = args.Get("--class");

        if (title is null && process is null && className is null)
        {
            throw new UsageException("window find needs at least one of --title, --process, --class or --hwnd.", Usage.Window);
        }

        var all = WindowList.List(includeInvisible, includeEmptyTitle);

        var matches = all
            .Where(w => title is null || Glob(w.Title, title))
            .Where(w => process is null || Glob(w.Process, process))
            .Where(w => className is null || Glob(w.Class, className))
            .ToList();

        if (matches.Count == 0)
        {
            throw new CliError(
                "NotFound",
                "No window matched.",
                context: new JsonObject
                {
                    ["title"] = title,
                    ["process"] = process,
                    ["class"] = className,
                    ["visibleCandidates"] = ToArray(all.Take(10).ToList()),
                },
                hints: ["run 'window list' to see what is on screen", "globs use * and ?"]);
        }

        if (index is null)
        {
            if (matches.Count > 1)
            {
                throw new CliError(
                    "Ambiguous",
                    $"{matches.Count} windows matched; pass --index 0..{matches.Count - 1}.",
                    context: new JsonObject { ["candidates"] = ToArray(matches.Take(10).ToList()) },
                    hints: ["the match list is ordered foreground-first, then by z-order"]);
            }
        }
        else if (index < 0 || index >= matches.Count)
        {
            throw new CliError(
                "InvalidArgument",
                $"--index {index} is out of range for {matches.Count} matches.",
                1);
        }

        var chosen = index is null ? matches[0] : matches[index.Value];

        var response = new Response("window find");
        response.Root["windows"] = ToArray([chosen]);
        response.Root["matchCount"] = matches.Count;
        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static int MoveToForeground(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "window move-to-foreground", Usage.Window, FocusValues, FocusFlags);

        var handle = ResolveHandle(args);
        var before = WindowList.Get(handle);
        var after = WindowList.Focus(handle);

        var response = new Response("window move-to-foreground");
        response.Root["target"] = Targets.WindowTarget(after);

        if (before.Rect != after.Rect)
        {
            response.Root["rectChanged"] = new JsonObject
            {
                ["before"] = Out.Rect(before.Rect),
                ["after"] = Out.Rect(after.Rect),
            };

            response.Warn("the window rectangle changed while it was brought forward; re-read it, do not reuse old coordinates");
        }

        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static int Wait(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "window wait", Usage.Window, WaitValues, WaitFlags);

        var appear = args.Flag("--appear");
        var disappear = args.Flag("--disappear");

        if (appear == disappear)
        {
            throw new UsageException("Pass exactly one of --appear or --disappear.", Usage.Window);
        }

        var timeoutSeconds = args.IntOr("--timeout", 30);
        var pollMs = Math.Clamp(args.IntOr("--poll-ms", 100), 10, 2000);
        var selector = args.Get("--window");
        var hwndText = args.Get("--hwnd");
        var title = args.Get("--title");
        var process = args.Get("--process");
        var className = args.Get("--class");
        var includeInvisible = args.Flag("--all");
        var includeEmptyTitle = args.Flag("--empty-title");

        if (selector is null && hwndText is null && title is null && process is null && className is null)
        {
            throw new UsageException(
                "window wait needs one of --window, --hwnd, --title, --process or --class.",
                Usage.Window);
        }

        var watch = Stopwatch.StartNew();
        WindowInfo? last = null;

        while (watch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            last = title is null && process is null && className is null
                ? TryResolve(selector, hwndText, args.Int("--index"))
                : WindowList.List(includeInvisible, includeEmptyTitle)
                    .FirstOrDefault(w =>
                        (title is null || Glob(w.Title, title))
                        && (process is null || Glob(w.Process, process))
                        && (className is null || Glob(w.Class, className)));

            if (appear && last is not null)
            {
                var found = new Response("window wait");
                found.Root["elapsedMs"] = watch.ElapsedMilliseconds;
                found.Root["windows"] = ToArray([last]);
                return found.Emit();
            }

            if (disappear && last is null)
            {
                var gone = new Response("window wait");
                gone.Root["elapsedMs"] = watch.ElapsedMilliseconds;
                gone.Root["windows"] = new JsonArray();
                return gone.Emit();
            }

            Thread.Sleep(pollMs);
        }

        throw new CliError(
            "Timeout",
            appear
                ? $"No window appeared within {timeoutSeconds}s."
                : $"The window was still there after {timeoutSeconds}s.",
            context: new JsonObject
            {
                ["elapsedMs"] = watch.ElapsedMilliseconds,
                ["lastSeen"] = last is null ? null : Out.Window(last),
            });
    }

    private static int PointInfo(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "window point-info", Usage.Window, PointValues, PointFlags);
        var point = args.RequirePoint("--at");

        WindowInfo? reference = null;
        if (args.Get("--window") is { } selector)
        {
            reference = WindowList.Resolve(selector, args.Int("--index"));
        }

        var space = reference is null ? Space.Desktop(null) : Space.ForWindow(reference);
        var screen = space.ToScreen(point);

        var hit = WindowList.FromPoint(screen, out var hitClass);

        var info = new JsonObject
        {
            ["at"] = Out.Point(point),
            ["space"] = space.Kind,
            ["screen"] = Out.Point(screen),
            ["monitor"] = Monitors.At(screen)?.Index,
            ["hitClass"] = hitClass,
        };

        if (hit is null)
        {
            info["hwnd"] = null;
            info["insideTarget"] = reference is null ? null : false;
        }
        else
        {
            info["hwnd"] = hit.Hwnd;
            info["class"] = hit.Class;
            info["title"] = hit.Title;
            info["process"] = hit.Process;
            info["rect"] = Out.Rect(hit.Rect);
            info["insideTarget"] = reference is null ? null : hit.Handle == reference.Handle;
        }

        var response = new Response("window point-info");
        response.Root["point"] = info;

        if (reference is not null)
        {
            response.Root["target"] = Targets.WindowTarget(reference);
        }

        response.Pointer(Pointer.Get());
        return response.Emit();
    }

    private static WindowInfo? TryResolve(string? selector, string? hwndText, int? index)
    {
        try
        {
            if (hwndText is not null)
            {
                var handle = WindowList.ParseHwnd(hwndText);
                return WindowList.Get(handle);
            }

            return WindowList.Resolve(selector!, index);
        }
        catch (CliError error) when (error.Code is "NotFound" or "InvalidWindow")
        {
            return null;
        }
    }

    private static nint ResolveHandle(Args args)
    {
        if (args.Get("--hwnd") is { } hwndText)
        {
            return WindowList.ParseHwnd(hwndText);
        }

        if (args.Get("--window") is { } selector)
        {
            return WindowList.Resolve(selector, args.Int("--index")).Handle;
        }

        throw new UsageException("Pass --hwnd <h> or --window <sel>.", Usage.Window);
    }

    private static JsonArray ToArray(IReadOnlyList<WindowInfo> windows)
    {
        var array = new JsonArray();
        foreach (var window in windows)
        {
            array.Add((JsonNode)Out.Window(window));
        }

        return array;
    }

    private static bool Glob(string value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(
            value ?? string.Empty,
            regex,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
