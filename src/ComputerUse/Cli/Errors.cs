using System.Text.Json.Nodes;

namespace ComputerUse.Cli;

/// <summary>
/// A failure reported as structured JSON. Exit code 1 = the caller asked for something
/// impossible; 2 = the machine refused. Failures are always explicit — nothing that
/// half-happened is reported as ok.
/// </summary>
internal sealed class CliError : Exception
{
    public CliError(
        string code,
        string message,
        int exitCode = 2,
        JsonObject? context = null,
        string[]? hints = null)
        : base(message)
    {
        Code = code;
        ExitCode = exitCode;
        Context = context;
        Hints = hints;
    }

    public string Code { get; }

    public int ExitCode { get; }

    public JsonObject? Context { get; }

    public string[]? Hints { get; }
}

/// <summary>Bad command line: unknown option, missing value, malformed coordinate pair.</summary>
internal sealed class UsageException(string message, string? help = null) : Exception(message)
{
    public string? Help { get; } = help;
}
