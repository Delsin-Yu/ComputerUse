using ComputerUse.Commands;
using ComputerUse.Diag;

namespace ComputerUse.Cli;

internal static class Dispatch
{
    /// <summary>Command path of the current invocation, used for error envelopes.</summary>
    public static string LastCommand { get; set; } = "computer-use";

    public static int Run(string[] argv)
    {
        try
        {
            if (argv.Length == 0)
            {
                throw new UsageException("No command given.", Usage.Root);
            }

            var verb = argv[0].ToLowerInvariant();
            LastCommand = verb;

            return verb switch
            {
                "--help" or "-h" or "help" => Print(Usage.Root),
                "screenshot" => ScreenshotCmd.Run(argv, 1),
                "screen" => Screen(argv),
                "window" => Group(argv, "window", Usage.Window, WindowCmds.Run),
                "mouse" => Group(argv, "mouse", Usage.Mouse, MouseCmds.Run),
                "keyboard" => Group(argv, "keyboard", Usage.Keyboard, KeyboardCmds.Run),
                _ => throw Unknown(verb),
            };
        }
        catch (UsageException error)
        {
            return Out.FailUsage(LastCommand, error);
        }
        catch (CliError error)
        {
            return Out.Fail(LastCommand, error);
        }
        catch (Exception error)
        {
            return Out.Fail(LastCommand, "RuntimeError", error.Message);
        }
    }

    /// <summary>
    /// Subcommands belong to a group, but people (and agents) reach for the leaf name on its
    /// own. Saying which command was meant is more useful than listing the whole grammar.
    /// </summary>
    private static readonly Dictionary<string, (string Path, string Help)> Subcommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["info"] = ("screen info", Usage.Screen),
            ["run-probe"] = ("screen run-probe", Usage.Screen),
            ["list"] = ("window list", Usage.Window),
            ["find"] = ("window find", Usage.Window),
            ["move-to-foreground"] = ("window move-to-foreground", Usage.Window),
            ["wait"] = ("window wait", Usage.Window),
            ["point-info"] = ("window point-info", Usage.Window),
            ["move"] = ("mouse move", Usage.Mouse),
            ["click"] = ("mouse click", Usage.Mouse),
            ["drag"] = ("mouse drag", Usage.Mouse),
            ["scroll"] = ("mouse scroll", Usage.Mouse),
            ["down"] = ("mouse down", Usage.Mouse),
            ["up"] = ("mouse up", Usage.Mouse),
            ["type"] = ("keyboard type", Usage.Keyboard),
            ["press"] = ("keyboard press", Usage.Keyboard),
        };

    private static UsageException Unknown(string verb)
    {
        if (Subcommands.TryGetValue(verb, out var entry))
        {
            LastCommand = entry.Path;
            return new UsageException(
                $"'{verb}' is a subcommand: use '{entry.Path}'.",
                entry.Help);
        }

        return new UsageException($"Unknown command '{verb}'.", Usage.Root);
    }

    private static int Print(string text)
    {
        Console.Out.Write(text);
        Console.Out.Write('\n');
        return 0;
    }

    private static int Group(string[] argv, string name, string help, Func<string[], int, string, int> handler)
    {
        if (argv.Length < 2)
        {
            throw new UsageException($"'{name}' needs a subcommand.", help);
        }

        var sub = argv[1].ToLowerInvariant();
        LastCommand = name + " " + sub;
        return handler(argv, 2, sub);
    }

    private static int Screen(string[] argv)
    {
        if (argv.Length >= 2 && argv[1].StartsWith("--", StringComparison.Ordinal))
        {
            // 'screen' is a group; people reach for it when they mean 'screenshot'.
            throw new UsageException(
                "'screen' needs a subcommand: info | run-probe. Did you mean 'screenshot'?",
                Usage.Screen);
        }

        if (argv.Length < 2)
        {
            throw new UsageException("'screen' needs a subcommand: info | run-probe.", Usage.Screen);
        }

        var sub = argv[1].ToLowerInvariant();
        LastCommand = "screen " + sub;

        return sub switch
        {
            "info" => ScreenCmd.Info(argv, 2),
            "run-probe" => Probe.Run(argv, 2),
            _ => throw new UsageException(
                $"Unknown screen subcommand '{argv[1]}'. Expected info or run-probe. Did you mean 'screenshot'?",
                Usage.Screen),
        };
    }
}
