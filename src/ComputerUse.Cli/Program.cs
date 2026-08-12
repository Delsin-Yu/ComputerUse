using System.CommandLine;
using ComputerUse.Cli.Commands;
using ComputerUse.Cli.Json;

namespace ComputerUse.Cli;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (!OperatingSystem.IsWindows())
        {
            return ResponseWriter.Fail("computer-use",
                new CliException("UnsupportedPlatform", "computer-use only supports Windows.", exitCode: 2));
        }

        var root = CommandRoot.Build();
        var parseResult = root.Parse(args);

        // Surface parse/arity errors as JSON when a command path is recognizable.
        if (parseResult.Errors.Count > 0)
        {
            var command = InferCommand(args);
            var message = string.Join("; ", parseResult.Errors.Select(e => e.Message));
            var help = IsMissingOrUnknownCommand(parseResult)
                ? InferHelp(args)
                : null;
            return ResponseWriter.FailParse(command, message, help);
        }

        return parseResult.Invoke();
    }

    private static string InferCommand(string[] args)
    {
        var parts = args.Where(a => !a.StartsWith('-')).Take(2).ToArray();
        return parts.Length == 0 ? "computer-use" : string.Join(' ', parts);
    }

    private static bool IsMissingOrUnknownCommand(ParseResult parseResult)
    {
        // No verb / unknown verb stays on the root command.
        if (parseResult.CommandResult.Command is RootCommand)
        {
            return true;
        }

        // Group without a subcommand (e.g. `mouse`).
        return parseResult.Errors.Any(e =>
        {
            var m = e.Message;
            return m.Contains("required command", StringComparison.OrdinalIgnoreCase)
                || m.Contains("必需的命令", StringComparison.Ordinal)
                || m.Contains("Unrecognized command", StringComparison.OrdinalIgnoreCase)
                || m.Contains("未识别命令", StringComparison.Ordinal);
        });
    }

    private static string InferHelp(string[] args)
    {
        var knownGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cursor", "mouse", "key", "screenshot", "screen", "window", "uia",
        };

        var parts = args.Where(a => !a.StartsWith('-')).Take(1).ToArray();
        if (parts.Length == 1 && knownGroups.Contains(parts[0]))
        {
            return $"computer-use {parts[0]} --help";
        }

        return "computer-use --help";
    }
}
