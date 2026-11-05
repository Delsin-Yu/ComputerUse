using ComputerUse.Cli;
using ComputerUse.Geo;

namespace ComputerUse.Input;

internal enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// Mouse clicks are two things at once: put the pointer on the target (verified) and only then
/// press. Down and up travel in a single SendInput call so nothing can slip between them.
/// </summary>
internal static class Mouse
{
    public static MouseButton ParseButton(string? name)
    {
        return (name ?? "left").Trim().ToLowerInvariant() switch
        {
            "left" or "l" => MouseButton.Left,
            "right" or "r" => MouseButton.Right,
            "middle" or "m" => MouseButton.Middle,
            _ => throw new UsageException($"Unknown button '{name}'. Use left, right or middle."),
        };
    }

    public static (uint Down, uint Up) Flags(MouseButton button) => button switch
    {
        MouseButton.Left => (NativeInput.MouseLeftDown, NativeInput.MouseLeftUp),
        MouseButton.Right => (NativeInput.MouseRightDown, NativeInput.MouseRightUp),
        MouseButton.Middle => (NativeInput.MouseMiddleDown, NativeInput.MouseMiddleUp),
        _ => throw new CliError("InvalidArgument", $"Unsupported button '{button}'.", 1),
    };

    /// <summary>Verifies the landing point, then presses and releases.</summary>
    public static MoveReport Click(PointI target, MouseButton button, int clicks, int tolerance, int delayMs)
    {
        if (clicks is < 1 or > 10)
        {
            throw new UsageException("--clicks must be between 1 and 10.");
        }

        var report = Pointer.Require(target, tolerance, "click");
        var (down, up) = Flags(button);

        for (var i = 0; i < clicks; i++)
        {
            NativeInput.Send(
                NativeInput.MouseEvent(down),
                NativeInput.MouseEvent(up));

            if (i + 1 < clicks && delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }

        return report;
    }

    public static void Down(MouseButton button)
    {
        var (down, _) = Flags(button);
        NativeInput.Send(NativeInput.MouseEvent(down));
    }

    public static void Up(MouseButton button)
    {
        var (_, up) = Flags(button);
        NativeInput.Send(NativeInput.MouseEvent(up));
    }

    public static void Wheel(int delta, int? horizontal = null)
    {
        if (delta == 0 && (horizontal is null or 0))
        {
            throw new UsageException("--delta must be non-zero.");
        }

        var events = new List<NativeInput.INPUT>(2);

        if (delta != 0)
        {
            events.Add(NativeInput.MouseEvent(NativeInput.MouseWheel, data: unchecked((uint)delta)));
        }

        if (horizontal is { } hx && hx != 0)
        {
            events.Add(NativeInput.MouseEvent(NativeInput.MouseHWheel, data: unchecked((uint)hx)));
        }

        NativeInput.Send(events.ToArray());
    }
}
