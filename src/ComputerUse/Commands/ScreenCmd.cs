using System.Text.Json.Nodes;
using ComputerUse.Cli;
using ComputerUse.Geo;
using ComputerUse.Input;

namespace ComputerUse.Commands;

internal static class ScreenCmd
{
    private static readonly HashSet<string> NoValues = new(StringComparer.Ordinal);

    private static readonly HashSet<string> NoFlags = new(StringComparer.Ordinal);

    public static int Info(string[] argv, int start)
    {
        Dispatch.LastCommand = "screen info";
        _ = Args.Parse(argv, start, "screen info", Usage.Screen, NoValues, NoFlags);

        var pointer = Pointer.Get();
        var monitors = new JsonArray();
        foreach (var monitor in Monitors.All())
        {
            monitors.Add((System.Text.Json.Nodes.JsonNode)Out.Monitor(monitor));
        }

        var response = new Response("screen info");
        response.Root["awareness"] = Dpi.Awareness;
        response.Root["virtualBounds"] = Out.Rect(Monitors.VirtualBounds());
        response.Root["monitors"] = monitors;
        response.Pointer(pointer);
        return response.Emit();
    }
}
