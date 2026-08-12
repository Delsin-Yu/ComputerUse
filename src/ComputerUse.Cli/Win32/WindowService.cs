using System.Text;
using ComputerUse.Cli.Json;

namespace ComputerUse.Cli.Win32;

internal static class WindowService
{
    public static IReadOnlyList<WindowDto> List(bool includeEmptyTitle, bool all)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var windows = new List<WindowDto>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd, includeEmptyTitle, all))
            {
                return true;
            }

            windows.Add(Describe(hwnd, foreground));
            return true;
        }, nint.Zero);

        windows.Sort((a, b) =>
        {
            var fg = b.Foreground.CompareTo(a.Foreground);
            return fg != 0 ? fg : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });

        return windows;
    }

    public static void EnsureWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !NativeMethods.IsWindow(hwnd))
        {
            throw new CliException("InvalidArgument", $"Window {HwndFormat.ToString(hwnd)} does not exist.", exitCode: 1);
        }
    }

    public static WindowDto Get(nint hwnd)
    {
        EnsureWindow(hwnd);
        return Describe(hwnd, NativeMethods.GetForegroundWindow());
    }

    public static IReadOnlyList<WindowDto> Focus(nint hwnd)
    {
        EnsureWindow(hwnd);

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != hwnd)
        {
            var targetTid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            var foregroundTid = foreground == nint.Zero
                ? 0u
                : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var currentTid = NativeMethods.GetCurrentThreadId();

            var attachedToForeground = false;
            var attachedToTarget = false;
            try
            {
                if (foregroundTid != 0 && foregroundTid != currentTid)
                {
                    attachedToForeground = NativeMethods.AttachThreadInput(currentTid, foregroundTid, true);
                }

                if (targetTid != 0 && targetTid != currentTid && targetTid != foregroundTid)
                {
                    attachedToTarget = NativeMethods.AttachThreadInput(currentTid, targetTid, true);
                }

                _ = NativeMethods.BringWindowToTop(hwnd);
                _ = NativeMethods.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attachedToTarget)
                {
                    NativeMethods.AttachThreadInput(currentTid, targetTid, false);
                }

                if (attachedToForeground)
                {
                    NativeMethods.AttachThreadInput(currentTid, foregroundTid, false);
                }
            }

            foreground = NativeMethods.GetForegroundWindow();
            if (foreground != hwnd)
            {
                throw new CliException(
                    "Win32Error",
                    $"Failed to bring window {HwndFormat.ToString(hwnd)} to the foreground.",
                    exitCode: 2);
            }
        }

        return [Describe(hwnd, foreground)];
    }

    private static bool IsCandidate(nint hwnd, bool includeEmptyTitle, bool all)
    {
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != nint.Zero)
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        var visible = NativeMethods.IsWindowVisible(hwnd);
        var minimized = NativeMethods.IsIconic(hwnd);
        if (!all && (!visible || minimized))
        {
            return false;
        }

        var title = GetWindowText(hwnd);
        if (!includeEmptyTitle && string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return true;
    }

    private static WindowDto Describe(nint hwnd, nint foreground)
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);

        return new WindowDto
        {
            Hwnd = HwndFormat.ToString(hwnd),
            Title = GetWindowText(hwnd),
            ClassName = GetClassName(hwnd),
            ProcessId = unchecked((int)pid),
            ProcessName = GetProcessName(pid),
            Bounds = new RectDto
            {
                X = rect.Left,
                Y = rect.Top,
                Width = Math.Max(0, rect.Right - rect.Left),
                Height = Math.Max(0, rect.Bottom - rect.Top),
            },
            Foreground = hwnd == foreground,
            Visible = NativeMethods.IsWindowVisible(hwnd),
            Minimized = NativeMethods.IsIconic(hwnd),
        };
    }

    private static string GetWindowText(nint hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        _ = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetProcessName(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            var size = 1024;
            var sb = new StringBuilder(size);
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size))
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(sb.ToString());
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
