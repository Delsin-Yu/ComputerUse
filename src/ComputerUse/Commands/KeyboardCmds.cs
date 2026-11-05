using System.Text;
using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Input;

namespace ComputerUse.Commands;

internal static class KeyboardCmds
{
    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        "--window", "--index", "--text", "--text-file", "--text-b64",
        "--delay-ms", "--method", "--hold", "--release", "--repeat",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal);

    public static int Run(string[] argv, int start, string verb)
    {
        Dispatch.LastCommand = "keyboard " + verb;

        return verb switch
        {
            "type" => Type(argv, start),
            "press" => Press(argv, start),
            _ => throw new UsageException($"Unknown keyboard verb '{verb}'.", Usage.Keyboard),
        };
    }

    private static int Type(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "keyboard type", Usage.Keyboard, Values, Flags);
        var window = Targets.FocusWindow(args);

        var sources = new[] { "--text", "--text-file", "--text-b64" }.Where(args.Has).ToArray();
        if (sources.Length != 1)
        {
            throw new UsageException("Give exactly one of --text, --text-file or --text-b64.", Usage.Keyboard);
        }

        var text = LoadText(args, sources[0]);
        var method = (args.Get("--method") ?? "unicode").ToLowerInvariant();
        if (method is not ("unicode" or "scan"))
        {
            throw new UsageException("--method must be unicode or scan.", Usage.Keyboard);
        }

        var delay = args.IntOr("--delay-ms", 0);

        Keyboard.TypeText(text, delay, method);

        var response = new Response("keyboard type");
        response.Root["target"] = window is null
            ? new JsonObject { ["kind"] = "focused" }
            : Targets.WindowTarget(window);

        response.Root["input"] = new JsonObject
        {
            ["text"] = text.Length <= 200 ? text : text[..200],
            ["length"] = text.Length,
            ["method"] = method,
            ["delayMs"] = delay,
        };

        return response.Emit();
    }

    private static int Press(string[] argv, int start)
    {
        var args = Args.Parse(argv, start, "keyboard press", Usage.Keyboard, Values, Flags);
        var window = Targets.FocusWindow(args);

        var hold = args.Get("--hold");
        var release = args.Get("--release");

        if (hold is not null && release is not null)
        {
            throw new UsageException("Use --hold or --release, not both.", Usage.Keyboard);
        }

        if ((hold is not null || release is not null) && args.Positionals.Count > 0)
        {
            throw new UsageException("Give either a key combo or --hold/--release, not both.", Usage.Keyboard);
        }

        var response = new Response("keyboard press");
        response.Root["target"] = window is null
            ? new JsonObject { ["kind"] = "focused" }
            : Targets.WindowTarget(window);

        if (hold is not null)
        {
            Keyboard.Down(hold);
            response.Root["input"] = new JsonObject { ["hold"] = hold };
            return response.Emit();
        }

        if (release is not null)
        {
            Keyboard.Up(release);
            response.Root["input"] = new JsonObject { ["release"] = release };
            return response.Emit();
        }

        if (args.Positionals.Count != 1)
        {
            throw new UsageException("keyboard press needs one key combo, for example 'ctrl+shift+t'.", Usage.Keyboard);
        }

        var combo = args.Positionals[0];
        var repeat = args.IntOr("--repeat", 1);
        var delay = args.IntOr("--delay-ms", 0);

        Keyboard.Press(combo, repeat, delay);

        response.Root["input"] = new JsonObject
        {
            ["combo"] = combo,
            ["repeat"] = repeat,
            ["delayMs"] = delay,
        };

        return response.Emit();
    }

    private static string LoadText(Args args, string source)
    {
        switch (source)
        {
            case "--text":
                return args.Get(source) ?? string.Empty;

            case "--text-file":
            {
                var path = args.Get(source)!;
                if (!File.Exists(path))
                {
                    throw new CliError("NotFound", $"Text file '{path}' does not exist.", 1);
                }

                return File.ReadAllText(path, Encoding.UTF8);
            }

            default:
            {
                var encoded = args.Get(source) ?? string.Empty;
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                catch (FormatException)
                {
                    throw new UsageException("--text-b64 is not valid base64.", Usage.Keyboard);
                }
            }
        }
    }
}
