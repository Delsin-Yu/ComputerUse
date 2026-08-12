namespace ComputerUse.Cli.Win32;

internal static class CursorService
{
    public static (int X, int Y) GetPosition()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            throw new CliException("Win32Error", "GetCursorPos failed.", exitCode: 2);
        }

        return (point.X, point.Y);
    }

    public static void Move(int x, int y) => InputService.MoveAbsolutePublic(x, y);
}
