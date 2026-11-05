using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse;

internal static class Program
{
    private static int Main(string[] argv)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Out.Fail("computer-use", "UnsupportedPlatform", "computer-use only supports Windows.");
        }

        try
        {
            Dpi.Ensure();
        }
        catch (CliError error)
        {
            return Out.Fail("computer-use", error);
        }

        return Dispatch.Run(argv);
    }
}
