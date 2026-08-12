using ComputerUse.Cli.Json;

namespace ComputerUse.Cli.Win32;

internal static class ScreenService
{
    public static RectDto GetVirtualBounds()
    {
        return new RectDto
        {
            X = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            Y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            Width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            Height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN),
        };
    }

    public static ScreenInfoDto GetInfo()
    {
        var monitors = new List<MonitorDto>();
        var index = 0;

        NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero,
            (nint hMonitor, nint _, ref NativeMethods.RECT _, nint _) =>
            {
                var info = new NativeMethods.MONITORINFOEX
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                    szDevice = string.Empty,
                };

                if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    return true;
                }

                monitors.Add(new MonitorDto
                {
                    Index = index++,
                    Name = string.IsNullOrWhiteSpace(info.szDevice) ? $"\\\\.\\DISPLAY{index}" : info.szDevice,
                    Primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    Bounds = ToRect(info.rcMonitor),
                    WorkArea = ToRect(info.rcWork),
                });

                return true;
            },
            nint.Zero);

        monitors.Sort((a, b) => a.Index.CompareTo(b.Index));

        return new ScreenInfoDto
        {
            VirtualBounds = GetVirtualBounds(),
            Monitors = monitors,
        };
    }

    public static RectDto ResolveCaptureRegion(int? x, int? y, int? width, int? height, int? monitorIndex)
    {
        RectDto region;
        if (monitorIndex is not null)
        {
            var info = GetInfo();
            var monitor = info.Monitors.FirstOrDefault(m => m.Index == monitorIndex.Value)
                ?? throw new CliException("InvalidArgument", $"Monitor index {monitorIndex.Value} was not found.");

            if (x is null && y is null && width is null && height is null)
            {
                region = monitor.Bounds;
            }
            else
            {
                var mx = x ?? 0;
                var my = y ?? 0;
                var mw = width ?? (monitor.Bounds.Width - mx);
                var mh = height ?? (monitor.Bounds.Height - my);

                region = new RectDto
                {
                    X = monitor.Bounds.X + mx,
                    Y = monitor.Bounds.Y + my,
                    Width = mw,
                    Height = mh,
                };
            }
        }
        else
        {
            var virtualBounds = GetVirtualBounds();
            region = new RectDto
            {
                X = x ?? virtualBounds.X,
                Y = y ?? virtualBounds.Y,
                Width = width ?? virtualBounds.Width,
                Height = height ?? virtualBounds.Height,
            };
        }

        return ClampToVirtual(region);
    }

    public static RectDto FromHwnd(nint hwnd)
    {
        WindowService.EnsureWindow(hwnd);
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return ClampToVirtual(ToRect(rect));
    }

    public static RectDto FromCenterSize(int centerX, int centerY, int size)
    {
        if (size < 1)
        {
            throw new CliException("InvalidArgument", "--size must be >= 1.");
        }

        var half = size / 2;
        return ClampToVirtual(new RectDto
        {
            X = centerX - half,
            Y = centerY - half,
            Width = size,
            Height = size,
        });
    }

    public static RectDto Inflate(RectDto rect, int pad)
    {
        if (pad < 0)
        {
            throw new CliException("InvalidArgument", "--pad must be >= 0.");
        }

        if (pad == 0)
        {
            return ClampToVirtual(rect);
        }

        return ClampToVirtual(new RectDto
        {
            X = rect.X - pad,
            Y = rect.Y - pad,
            Width = rect.Width + pad * 2,
            Height = rect.Height + pad * 2,
        });
    }

    public static RectDto ClampToVirtual(RectDto rect)
    {
        var screen = GetVirtualBounds();
        var left = Math.Max(rect.X, screen.X);
        var top = Math.Max(rect.Y, screen.Y);
        var right = Math.Min(rect.X + Math.Max(0, rect.Width), screen.X + screen.Width);
        var bottom = Math.Min(rect.Y + Math.Max(0, rect.Height), screen.Y + screen.Height);
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);

        if (width <= 0 || height <= 0)
        {
            throw new CliException("InvalidArgument", "Capture region is empty after clamping to the virtual screen.");
        }

        return new RectDto
        {
            X = left,
            Y = top,
            Width = width,
            Height = height,
        };
    }

    private static RectDto ToRect(NativeMethods.RECT rect) => new()
    {
        X = rect.Left,
        Y = rect.Top,
        Width = Math.Max(0, rect.Right - rect.Left),
        Height = Math.Max(0, rect.Bottom - rect.Top),
    };
}
