using System.Runtime.InteropServices;
using ComputerUse.Cli.Json;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Uia;

internal sealed class ResolvedRef : IDisposable
{
    public required nint Element { get; init; }
    public required SnapshotElementDto Info { get; init; }

    public void Dispose()
    {
        UiaCom.Release(Element);
    }
}

internal static class UiaActionService
{
    public static SnapshotOptions DefaultLookupOptions { get; } = new()
    {
        View = SnapshotView.Control,
        InteractiveOnly = false,
        Depth = 8,
        MaxNodes = 500,
        Format = SnapshotFormat.Json,
    };

    public static ResolvedRef AcquireByRef(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        options ??= DefaultLookupOptions;
        var targetRef = ParseRef(refText);
        WindowService.EnsureWindow(hwnd);

        var automation = nint.Zero;
        var rootElement = nint.Zero;
        var walker = nint.Zero;

        try
        {
            automation = UiaCom.CreateAutomation();
            rootElement = UiaVtable.ElementFromHandle(automation, hwnd);
            if (rootElement == nint.Zero)
            {
                throw new CliException("Win32Error", $"UI Automation could not bind hwnd {HwndFormat.ToString(hwnd)}.", exitCode: 2);
            }

            walker = options.View switch
            {
                SnapshotView.Content => UiaVtable.GetContentViewWalker(automation),
                SnapshotView.Raw => UiaVtable.GetRawViewWalker(automation),
                _ => UiaVtable.GetControlViewWalker(automation),
            };

            var nextRef = 1;
            var nodeCount = 0;
            var truncated = false;
            if (!TryFind(rootElement, walker, options, options.Depth, targetRef, ref nextRef, ref nodeCount, ref truncated, out var found, out var info)
                || found == nint.Zero
                || info is null)
            {
                throw new CliException(
                    "InvalidArgument",
                    $"Ref '{NormalizeRef(refText)}' was not found in hwnd {HwndFormat.ToString(hwnd)}. Re-run 'window snapshot' and use a current ref.",
                    exitCode: 1);
            }

            return new ResolvedRef { Element = found, Info = info };
        }
        finally
        {
            UiaCom.Release(walker);
            UiaCom.Release(rootElement);
            UiaCom.Release(automation);
        }
    }

    public static SnapshotElementDto Invoke(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.Invoke(resolved.Element);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto SetValue(nint hwnd, string refText, string value, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.SetValue(resolved.Element, value);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto Toggle(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.Toggle(resolved.Element);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto Select(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.Select(resolved.Element);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto Expand(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.Expand(resolved.Element);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto Collapse(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.Collapse(resolved.Element);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto Focus(nint hwnd, string refText, SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        UiaVtable.SetFocus(resolved.Element);
        return RefreshInfo(resolved);
    }

    public static SnapshotElementDto ClickRef(
        nint hwnd,
        string refText,
        MouseButton button,
        int clicks,
        SnapshotOptions? options = null)
    {
        using var resolved = AcquireByRef(hwnd, refText, options);
        var center = resolved.Info.Center;
        if (resolved.Info.Bounds.Width <= 0 || resolved.Info.Bounds.Height <= 0)
        {
            throw new CliException("InvalidArgument", $"Ref '{resolved.Info.Ref}' has empty bounds; cannot click.", exitCode: 1);
        }

        InputService.Click(button, center.X, center.Y, clicks);
        return resolved.Info;
    }

    public static string ParseRef(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliException("InvalidArgument", "--ref is required (e.g. e12).");
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('e') || trimmed.StartsWith('E'))
        {
            trimmed = trimmed[1..];
        }

        if (!int.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n)
            || n < 1)
        {
            throw new CliException("InvalidArgument", $"Invalid ref '{value}'. Use eN (e.g. e12).");
        }

        return $"e{n}";
    }

    private static string NormalizeRef(string value) => ParseRef(value);

    private static bool TryFind(
        nint element,
        nint walker,
        SnapshotOptions options,
        int remainingDepth,
        string targetRef,
        ref int nextRef,
        ref int nodeCount,
        ref bool truncated,
        out nint found,
        out SnapshotElementDto? info)
    {
        found = nint.Zero;
        info = null;

        if (nodeCount >= options.MaxNodes)
        {
            truncated = true;
            return false;
        }

        var dto = AriaSnapshotService.DescribeElement(element, options, ref nextRef, ref nodeCount);
        if (dto.Ref == targetRef)
        {
            _ = Marshal.AddRef(element);
            found = element;
            info = dto;
            return true;
        }

        if (remainingDepth <= 0 || nodeCount >= options.MaxNodes)
        {
            if (nodeCount >= options.MaxNodes)
            {
                truncated = true;
            }

            return false;
        }

        var child = UiaVtable.GetFirstChildElement(walker, element);
        while (child != nint.Zero)
        {
            if (nodeCount >= options.MaxNodes)
            {
                truncated = true;
                UiaCom.Release(child);
                break;
            }

            try
            {
                if (TryFind(child, walker, options, remainingDepth - 1, targetRef, ref nextRef, ref nodeCount, ref truncated, out found, out info))
                {
                    UiaCom.Release(child);
                    return true;
                }

                var next = UiaVtable.GetNextSiblingElement(walker, child);
                UiaCom.Release(child);
                child = next;
            }
            catch
            {
                UiaCom.Release(child);
                throw;
            }
        }

        return false;
    }

    private static SnapshotElementDto RefreshInfo(ResolvedRef resolved)
    {
        var options = DefaultLookupOptions;
        var nextRef = 1;
        var nodeCount = 0;
        // Keep the same ref id the caller used.
        var info = AriaSnapshotService.DescribeElement(resolved.Element, options, ref nextRef, ref nodeCount);
        return new SnapshotElementDto
        {
            Ref = resolved.Info.Ref,
            Role = info.Role,
            Name = info.Name,
            Value = info.Value,
            States = info.States,
            Bounds = info.Bounds,
            Center = info.Center,
            Actions = info.Actions,
        };
    }
}
