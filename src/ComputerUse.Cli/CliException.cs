namespace ComputerUse.Cli;

internal sealed class CliException : Exception
{
    public string Code { get; }
    public int ExitCode { get; }

    public CliException(string code, string message, int exitCode = 1)
        : base(message)
    {
        Code = code;
        ExitCode = exitCode;
    }
}
