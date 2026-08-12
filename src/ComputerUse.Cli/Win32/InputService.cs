using System.Runtime.InteropServices;

namespace ComputerUse.Cli.Win32;

internal enum MouseButton
{
    Left,
    Right,
    Middle,
}

internal static class InputService
{
    public static void MoveAbsolutePublic(int x, int y) => MoveAbsolute(x, y);

    public static void Click(MouseButton button, int? x, int? y, int clicks, int clickDelayMs = 50)
    {
        if (clicks is < 1 or > 10)
        {
            throw new CliException("InvalidArgument", "--clicks must be between 1 and 10.");
        }

        if (clickDelayMs < 0)
        {
            throw new CliException("InvalidArgument", "--click-delay-ms must be >= 0.");
        }

        if (x is not null || y is not null)
        {
            EnsureBothCoordinates(x, y);
            MoveAbsolute(x!.Value, y!.Value);
        }

        var (down, up) = ButtonFlags(button);
        for (var i = 0; i < clicks; i++)
        {
            Send(MouseEvent(down), MouseEvent(up));
            if (i + 1 < clicks && clickDelayMs > 0)
            {
                Thread.Sleep(clickDelayMs);
            }
        }
    }

    public static void ButtonDown(MouseButton button, int? x, int? y)
    {
        if (x is not null || y is not null)
        {
            EnsureBothCoordinates(x, y);
            MoveAbsolute(x!.Value, y!.Value);
        }

        var (down, _) = ButtonFlags(button);
        Send(MouseEvent(down));
    }

    public static void ButtonUp(MouseButton button, int? x, int? y)
    {
        if (x is not null || y is not null)
        {
            EnsureBothCoordinates(x, y);
            MoveAbsolute(x!.Value, y!.Value);
        }

        var (_, up) = ButtonFlags(button);
        Send(MouseEvent(up));
    }

    public static void Scroll(int delta, int? dx, int? x = null, int? y = null)
    {
        if (x is not null || y is not null)
        {
            EnsureBothCoordinates(x, y);
            MoveAbsolute(x!.Value, y!.Value);
        }

        var inputs = new List<NativeMethods.INPUT>();
        if (delta != 0)
        {
            inputs.Add(MouseEvent(NativeMethods.MOUSEEVENTF_WHEEL, mouseData: unchecked((uint)delta)));
        }

        if (dx is not null and not 0)
        {
            inputs.Add(MouseEvent(NativeMethods.MOUSEEVENTF_HWHEEL, mouseData: unchecked((uint)dx.Value)));
        }

        if (inputs.Count == 0)
        {
            throw new CliException("InvalidArgument", "Provide a non-zero --delta and/or --dx.");
        }

        Send(inputs.ToArray());
    }

    public static void Drag(int fromX, int fromY, int toX, int toY, MouseButton button, int steps = 12, int stepDelayMs = 8)
    {
        if (steps < 1)
        {
            throw new CliException("InvalidArgument", "--steps must be >= 1.");
        }

        if (stepDelayMs < 0)
        {
            throw new CliException("InvalidArgument", "--step-delay-ms must be >= 0.");
        }

        var (down, up) = ButtonFlags(button);
        MoveAbsolute(fromX, fromY);
        Send(MouseEvent(down));

        for (var i = 1; i <= steps; i++)
        {
            var t = i / (double)steps;
            var x = (int)Math.Round(fromX + (toX - fromX) * t);
            var y = (int)Math.Round(fromY + (toY - fromY) * t);
            MoveAbsolute(x, y);
            if (stepDelayMs > 0 && i < steps)
            {
                Thread.Sleep(stepDelayMs);
            }
        }

        Send(MouseEvent(up));
    }

    public static void TypeText(string text, int delayMs)
    {
        if (delayMs < 0)
        {
            throw new CliException("InvalidArgument", "--delay-ms must be >= 0.");
        }

        if (delayMs > 0)
        {
            foreach (var ch in text)
            {
                TypeOne(ch);
                Thread.Sleep(delayMs);
            }

            return;
        }

        // Batch Unicode key events for faster typing when no per-char delay is requested.
        var inputs = new List<NativeMethods.INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            if (ch is '\r')
            {
                continue;
            }

            if (ch is '\n')
            {
                if (inputs.Count > 0)
                {
                    Send(inputs.ToArray());
                    inputs.Clear();
                }

                TapKey(KeyMap.Parse("enter"), []);
                continue;
            }

            inputs.Add(UnicodeKey((ushort)ch, keyUp: false));
            inputs.Add(UnicodeKey((ushort)ch, keyUp: true));
            if (inputs.Count >= 64)
            {
                Send(inputs.ToArray());
                inputs.Clear();
            }
        }

        if (inputs.Count > 0)
        {
            Send(inputs.ToArray());
        }
    }

    private static void TypeOne(char ch)
    {
        if (ch is '\r')
        {
            return;
        }

        if (ch is '\n')
        {
            TapKey(KeyMap.Parse("enter"), []);
            return;
        }

        Send(
            UnicodeKey((ushort)ch, keyUp: false),
            UnicodeKey((ushort)ch, keyUp: true));
    }

    public static void TapKey(ushort key, IReadOnlyList<ushort> modifiers)
    {
        var inputs = new List<NativeMethods.INPUT>(modifiers.Count * 2 + 2);
        foreach (var mod in modifiers)
        {
            inputs.Add(VkKey(mod, keyUp: false));
        }

        inputs.Add(VkKey(key, keyUp: false));
        inputs.Add(VkKey(key, keyUp: true));

        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            inputs.Add(VkKey(modifiers[i], keyUp: true));
        }

        Send(inputs.ToArray());
    }

    public static void KeyDown(ushort key) => Send(VkKey(key, keyUp: false));

    public static void KeyUp(ushort key) => Send(VkKey(key, keyUp: true));

    public static MouseButton ParseButton(string? button)
    {
        return (button ?? "left").Trim().ToLowerInvariant() switch
        {
            "left" or "l" => MouseButton.Left,
            "right" or "r" => MouseButton.Right,
            "middle" or "m" => MouseButton.Middle,
            _ => throw new CliException("InvalidArgument", $"Unknown mouse button '{button}'. Use left, right, or middle."),
        };
    }

    private static void MoveAbsolute(int x, int y) => Send(AbsoluteMove(x, y));

    private static NativeMethods.INPUT AbsoluteMove(int x, int y)
    {
        var bounds = ScreenService.GetVirtualBounds();
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            throw new CliException("Win32Error", "Virtual screen metrics are invalid.", exitCode: 2);
        }

        var absX = (int)Math.Round((x - bounds.X) * 65535.0 / (bounds.Width - 1));
        var absY = (int)Math.Round((y - bounds.Y) * 65535.0 / (bounds.Height - 1));

        return MouseEvent(
            NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK,
            absX,
            absY);
    }

    private static NativeMethods.INPUT MouseEvent(uint flags, int dx = 0, int dy = 0, uint mouseData = 0) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        U = new NativeMethods.INPUTUNION
        {
            mi = new NativeMethods.MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = mouseData,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = 0,
            },
        },
    };

    private static NativeMethods.INPUT VkKey(ushort vk, bool keyUp)
    {
        uint flags = 0;
        if (keyUp)
        {
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        }

        if (KeyMap.IsExtended(vk))
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)NativeMethods.MapVirtualKey(vk, 0),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                },
            },
        };
    }

    private static NativeMethods.INPUT UnicodeKey(ushort scan, bool keyUp)
    {
        uint flags = NativeMethods.KEYEVENTF_UNICODE;
        if (keyUp)
        {
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                },
            },
        };
    }

    private static (uint Down, uint Up) ButtonFlags(MouseButton button) => button switch
    {
        MouseButton.Left => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP),
        MouseButton.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
        _ => throw new CliException("InvalidArgument", $"Unsupported mouse button '{button}'."),
    };

    private static void EnsureBothCoordinates(int? x, int? y)
    {
        if (x is null || y is null)
        {
            throw new CliException("InvalidArgument", "Both --x and --y must be provided together.");
        }
    }

    private static void Send(params NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            var error = Marshal.GetLastWin32Error();
            throw new CliException("Win32Error", $"SendInput failed (sent {sent}/{inputs.Length}, error {error}).", exitCode: 2);
        }
    }
}
