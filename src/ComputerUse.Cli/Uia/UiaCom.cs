using System.Runtime.InteropServices;

namespace ComputerUse.Cli.Uia;

internal static class UiaCom
{
    private static readonly Guid ClsidCuiAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid IidIuiAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
    private const uint ClsctxInprocServer = 1;

    public static nint CreateAutomation()
    {
        var clsid = ClsidCuiAutomation;
        var iid = IidIuiAutomation;
        var hr = CoCreateInstance(ref clsid, nint.Zero, ClsctxInprocServer, ref iid, out var punk);
        if (hr < 0 || punk == nint.Zero)
        {
            throw new CliException("Win32Error", $"CoCreateInstance(CUIAutomation) failed (HRESULT 0x{hr:X8}).", exitCode: 2);
        }

        return punk;
    }

    public static void Release(nint punk)
    {
        if (punk != nint.Zero)
        {
            Marshal.Release(punk);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out nint ppv);
}

internal static class UiaBstr
{
    public static string Read(nint bstr)
    {
        if (bstr == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringBSTR(bstr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }
}
