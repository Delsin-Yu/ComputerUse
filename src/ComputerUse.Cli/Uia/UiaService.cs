using ComputerUse.Cli.Json;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Uia;

internal static class UiaService
{
    public static UiaDumpDto Dump(nint hwnd, int depth, int maxNodes, bool visibleOnly)
    {
        if (depth < 0)
        {
            throw new CliException("InvalidArgument", "--depth must be >= 0.");
        }

        if (maxNodes < 1)
        {
            throw new CliException("InvalidArgument", "--max-nodes must be >= 1.");
        }

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

            walker = visibleOnly
                ? UiaVtable.GetControlViewWalker(automation)
                : UiaVtable.GetRawViewWalker(automation);

            var nextId = 1;
            var nodeCount = 0;
            var truncated = false;
            var root = BuildNode(rootElement, walker, depth, maxNodes, ref nextId, ref nodeCount, ref truncated);

            return new UiaDumpDto
            {
                Hwnd = HwndFormat.ToString(hwnd),
                Depth = depth,
                Truncated = truncated,
                Root = root,
            };
        }
        finally
        {
            UiaCom.Release(walker);
            UiaCom.Release(rootElement);
            UiaCom.Release(automation);
        }
    }

    private static UiaNodeDto BuildNode(
        nint element,
        nint walker,
        int remainingDepth,
        int maxNodes,
        ref int nextId,
        ref int nodeCount,
        ref bool truncated)
    {
        nodeCount++;
        var id = nextId++;
        var node = ReadNode(element, id);

        if (remainingDepth <= 0 || nodeCount >= maxNodes)
        {
            if (nodeCount >= maxNodes)
            {
                truncated = true;
            }

            node.Children = [];
            return node;
        }

        var children = new List<UiaNodeDto>();
        var child = UiaVtable.GetFirstChildElement(walker, element);
        while (child != nint.Zero)
        {
            if (nodeCount >= maxNodes)
            {
                truncated = true;
                UiaCom.Release(child);
                break;
            }

            try
            {
                children.Add(BuildNode(child, walker, remainingDepth - 1, maxNodes, ref nextId, ref nodeCount, ref truncated));
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

        node.Children = children;
        return node;
    }

    private static UiaNodeDto ReadNode(nint element, int id)
    {
        var rect = UiaVtable.GetCurrentBoundingRectangle(element);

        return new UiaNodeDto
        {
            Id = id,
            ControlType = UiaControlTypes.ToName(UiaVtable.GetCurrentControlType(element)),
            Name = UiaVtable.GetCurrentName(element),
            AutomationId = UiaVtable.GetCurrentAutomationId(element),
            ClassName = UiaVtable.GetCurrentClassName(element),
            Bounds = new RectDto
            {
                X = rect.Left,
                Y = rect.Top,
                Width = Math.Max(0, rect.Right - rect.Left),
                Height = Math.Max(0, rect.Bottom - rect.Top),
            },
            Enabled = UiaVtable.GetCurrentIsEnabled(element),
            Offscreen = UiaVtable.GetCurrentIsOffscreen(element),
            Children = [],
        };
    }
}
