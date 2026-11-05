using System.Runtime.InteropServices;
using ComputerUse.Cli;

namespace ComputerUse.Input;

/// <summary>Shared SendInput surface for mouse and keyboard.</summary>
internal static class NativeInput
{
    public const uint InputMouse = 0;
    public const uint InputKeyboard = 1;

    public const uint MouseMove = 0x0001;
    public const uint MouseLeftDown = 0x0002;
    public const uint MouseLeftUp = 0x0004;
    public const uint MouseRightDown = 0x0008;
    public const uint MouseRightUp = 0x0010;
    public const uint MouseMiddleDown = 0x0020;
    public const uint MouseMiddleUp = 0x0040;
    public const uint MouseWheel = 0x0800;
    public const uint MouseHWheel = 0x1000;
    public const uint MouseVirtualDesk = 0x4000;
    public const uint MouseAbsolute = 0x8000;

    public const uint KeyExtended = 0x0001;
    public const uint KeyUp = 0x0002;
    public const uint KeyUnicode = 0x0004;
    public const uint KeyScanCode = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetClipCursor(out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        var sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != inputs.Length)
        {
            throw new CliError(
                "InputNotAccepted",
                $"SendInput accepted {sent}/{inputs.Length} events (win32 {Marshal.GetLastWin32Error()}).",
                hints: ["another process may be holding the input queue; nothing was retried"]);
        }
    }

    public static INPUT MouseEvent(uint flags, int dx = 0, int dy = 0, uint data = 0) => new()
    {
        type = InputMouse,
        U = new INPUTUNION
        {
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                mouseData = data,
                dwFlags = flags,
            },
        },
    };

    public static INPUT KeyEvent(ushort vk, ushort scan, uint flags) => new()
    {
        type = InputKeyboard,
        U = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = scan,
                dwFlags = flags,
            },
        },
    };
}
