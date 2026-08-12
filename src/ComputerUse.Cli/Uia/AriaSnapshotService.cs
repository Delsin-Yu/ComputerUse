using System.Text;
using ComputerUse.Cli.Json;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Uia;

internal enum SnapshotView
{
    Control,
    Content,
    Raw,
}

internal enum SnapshotFormat
{
    Both,
    Yaml,
    Json,
}

internal sealed class SnapshotOptions
{
    public required SnapshotView View { get; init; }
    public required bool InteractiveOnly { get; init; }
    public required int Depth { get; init; }
    public required int MaxNodes { get; init; }
    public required SnapshotFormat Format { get; init; }
    public int MaxValueLength { get; init; } = 200;
}

internal static class AriaSnapshotService
{
    public static WindowSnapshotDto Snapshot(nint hwnd, SnapshotOptions options)
    {
        if (options.Depth < 0)
        {
            throw new CliException("InvalidArgument", "--depth must be >= 0.");
        }

        if (options.MaxNodes < 1)
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

            walker = options.View switch
            {
                SnapshotView.Content => UiaVtable.GetContentViewWalker(automation),
                SnapshotView.Raw => UiaVtable.GetRawViewWalker(automation),
                _ => UiaVtable.GetControlViewWalker(automation),
            };

            var nextRef = 1;
            var nodeCount = 0;
            var truncated = false;
            var root = BuildNode(rootElement, walker, options, options.Depth, ref nextRef, ref nodeCount, ref truncated);
            root = Prune(root, options.InteractiveOnly, isRoot: true);
            if (root.IsEmpty)
            {
                // Extremely sparse hosts (e.g. ApplicationFrameWindow): still emit the root shell.
                var keepRef = 1;
                var keepCount = 0;
                var keepTruncated = false;
                root = ReadNode(rootElement, options, ref keepRef, ref keepCount, ref keepTruncated);
            }

            var elements = new Dictionary<string, SnapshotElementDto>(StringComparer.Ordinal);
            CollectElements(root, elements);

            var yaml = options.Format is SnapshotFormat.Json
                ? null
                : SerializeYaml(root);

            var elementMap = options.Format is SnapshotFormat.Yaml
                ? null
                : elements;

            return new WindowSnapshotDto
            {
                Snapshot = yaml,
                Elements = elementMap,
                Meta = new SnapshotMetaDto
                {
                    Hwnd = HwndFormat.ToString(hwnd),
                    View = options.View.ToString().ToLowerInvariant(),
                    InteractiveOnly = options.InteractiveOnly,
                    Depth = options.Depth,
                    NodeCount = elements.Count,
                    Truncated = truncated,
                    Format = options.Format.ToString().ToLowerInvariant(),
                },
            };
        }
        finally
        {
            UiaCom.Release(walker);
            UiaCom.Release(rootElement);
            UiaCom.Release(automation);
        }
    }

    /// <summary>
    /// Assigns the next snapshot ref and builds the element DTO (same rules as window snapshot).
    /// </summary>
    internal static SnapshotElementDto DescribeElement(
        nint element,
        SnapshotOptions options,
        ref int nextRef,
        ref int nodeCount)
    {
        var truncated = false;
        var node = ReadNode(element, options, ref nextRef, ref nodeCount, ref truncated);
        return ToElementDto(node);
    }

    public static SnapshotElementDto FromPoint(int x, int y)
    {
        var automation = nint.Zero;
        var element = nint.Zero;

        try
        {
            automation = UiaCom.CreateAutomation();
            element = UiaVtable.ElementFromPoint(automation, x, y);
            if (element == nint.Zero)
            {
                throw new CliException("Win32Error", $"No UI Automation element at ({x}, {y}).", exitCode: 2);
            }

            var options = new SnapshotOptions
            {
                View = SnapshotView.Control,
                InteractiveOnly = false,
                Depth = 0,
                MaxNodes = 1,
                Format = SnapshotFormat.Json,
            };

            var nextRef = 1;
            var nodeCount = 0;
            var truncated = false;
            var node = ReadNode(element, options, ref nextRef, ref nodeCount, ref truncated);
            return ToElementDto(node);
        }
        finally
        {
            UiaCom.Release(element);
            UiaCom.Release(automation);
        }
    }

    private static SnapshotNode BuildNode(
        nint element,
        nint walker,
        SnapshotOptions options,
        int remainingDepth,
        ref int nextRef,
        ref int nodeCount,
        ref bool truncated)
    {
        if (nodeCount >= options.MaxNodes)
        {
            truncated = true;
            return SnapshotNode.Empty;
        }

        var node = ReadNode(element, options, ref nextRef, ref nodeCount, ref truncated);
        if (node.IsEmpty)
        {
            return node;
        }

        if (remainingDepth <= 0 || nodeCount >= options.MaxNodes)
        {
            if (nodeCount >= options.MaxNodes)
            {
                truncated = true;
            }

            return node;
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
                var childNode = BuildNode(child, walker, options, remainingDepth - 1, ref nextRef, ref nodeCount, ref truncated);
                if (!childNode.IsEmpty)
                {
                    node.Children.Add(childNode);
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

        return node;
    }

    private static SnapshotNode ReadNode(
        nint element,
        SnapshotOptions options,
        ref int nextRef,
        ref int nodeCount,
        ref bool truncated)
    {
        _ = truncated;
        nodeCount++;
        var controlType = UiaVtable.GetCurrentControlType(element);
        var rect = UiaVtable.GetCurrentBoundingRectangle(element);
        var width = Math.Max(0, rect.Right - rect.Left);
        var height = Math.Max(0, rect.Bottom - rect.Top);
        var name = NormalizeWhitespace(UiaVtable.GetCurrentName(element));
        var enabled = UiaVtable.GetCurrentIsEnabled(element);
        var offscreen = UiaVtable.GetCurrentIsOffscreen(element);
        var focused = UiaVtable.GetCurrentHasKeyboardFocus(element);
        var focusable = UiaVtable.GetCurrentIsKeyboardFocusable(element);
        var isPassword = UiaVtable.GetCurrentIsPassword(element);

        var states = new List<string>();
        if (!enabled)
        {
            states.Add("disabled");
        }

        if (focused)
        {
            states.Add("focused");
        }

        if (offscreen)
        {
            states.Add("offscreen");
        }

        string? value = null;
        var valueText = UiaVtable.TryGetValuePatternValue(element, out var valueReadOnly);
        var hasValuePattern = valueText is not null;
        if (hasValuePattern)
        {
            var normalized = isPassword ? "********" : Truncate(NormalizeWhitespace(valueText!), options.MaxValueLength);
            value = string.IsNullOrEmpty(normalized) ? null : normalized;
            if (valueReadOnly)
            {
                states.Add("readonly");
            }
        }
        else
        {
            var text = UiaVtable.TryGetTextPatternValue(element, options.MaxValueLength);
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = isPassword ? "********" : Truncate(NormalizeWhitespace(text), options.MaxValueLength);
            }
        }

        var toggle = UiaVtable.TryGetToggleState(element);
        if (toggle is 1)
        {
            states.Add("checked");
        }
        else if (toggle is 2)
        {
            states.Add("checked=mixed");
        }

        var selected = UiaVtable.TryGetIsSelected(element);
        if (selected is true)
        {
            states.Add("selected");
        }

        var expand = UiaVtable.TryGetExpandCollapseState(element);
        if (expand is 1)
        {
            states.Add("expanded");
        }
        else if (expand is 0)
        {
            states.Add("collapsed");
        }
        else if (expand is 2)
        {
            states.Add("expanded=partial");
        }

        var hasInvoke = UiaVtable.HasInvokePattern(element);
        var actions = new List<string>();
        if (focusable || focused)
        {
            actions.Add("focus");
        }

        if (hasInvoke || UiaControlTypes.IsTypicallyInteractive(controlType))
        {
            actions.Add("click");
        }

        if (hasInvoke)
        {
            actions.Add("invoke");
        }

        if (hasValuePattern && !valueReadOnly)
        {
            actions.Add("setValue");
            actions.Add("type");
        }
        else if (controlType is 50004 or 50030) // Edit / Document
        {
            actions.Add("type");
        }

        if (toggle is not null)
        {
            actions.Add("toggle");
        }

        if (selected is not null)
        {
            actions.Add("select");
        }

        if (expand is 0 or 2)
        {
            actions.Add("expand");
        }

        if (expand is 1 or 2)
        {
            actions.Add("collapse");
        }

        var refId = $"e{nextRef++}";
        return new SnapshotNode
        {
            Ref = refId,
            ControlTypeId = controlType,
            Role = UiaControlTypes.ToAriaRole(controlType),
            Name = name,
            Value = value,
            States = states,
            Actions = actions.Distinct(StringComparer.Ordinal).ToList(),
            Bounds = new RectDto
            {
                X = rect.Left,
                Y = rect.Top,
                Width = width,
                Height = height,
            },
            Center = new PointDto
            {
                X = rect.Left + width / 2,
                Y = rect.Top + height / 2,
            },
            Children = [],
        };
    }

    private static SnapshotNode Prune(SnapshotNode node, bool interactiveOnly, bool isRoot = false)
    {
        if (node.IsEmpty)
        {
            return node;
        }

        var keptChildren = new List<SnapshotNode>();
        foreach (var child in node.Children)
        {
            var pruned = Prune(child, interactiveOnly, isRoot: false);
            if (pruned.IsEmpty)
            {
                continue;
            }

            if (ShouldPromoteChildren(pruned))
            {
                keptChildren.AddRange(pruned.Children);
            }
            else
            {
                keptChildren.Add(pruned);
            }
        }

        node.Children = keptChildren;

        var zeroSize = node.Bounds.Width <= 0 || node.Bounds.Height <= 0;
        if (!isRoot && zeroSize && node.Children.Count == 0)
        {
            return SnapshotNode.Empty;
        }

        if (!isRoot && interactiveOnly && !IsActionable(node) && node.Children.Count == 0)
        {
            return SnapshotNode.Empty;
        }

        if (!isRoot && interactiveOnly && !IsActionable(node) && ShouldPromoteChildren(node))
        {
            return node.Children.Count == 1 ? node.Children[0] : node;
        }

        return node;
    }

    private static bool ShouldPromoteChildren(SnapshotNode node)
    {
        if (node.Children.Count == 0)
        {
            return false;
        }

        var unnamed = string.IsNullOrWhiteSpace(node.Name);
        var layout = UiaControlTypes.IsLayoutContainer(node.ControlTypeId)
            || node.Role is "pane" or "group" or "generic" or "titlebar";
        var noValue = string.IsNullOrEmpty(node.Value);
        var weakActions = node.Actions.Count == 0
            || (node.Actions.Count == 1 && node.Actions[0] == "focus");

        return unnamed && layout && noValue && weakActions;
    }

    private static bool IsActionable(SnapshotNode node)
    {
        if (UiaControlTypes.IsTypicallyInteractive(node.ControlTypeId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(node.Name) && node.Actions.Any(a => a is "click" or "invoke" or "setValue" or "type" or "toggle" or "select"))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(node.Value))
        {
            return true;
        }

        return node.States.Any(s => s is "focused" or "checked" or "selected" or "expanded" or "collapsed");
    }

    private static void CollectElements(SnapshotNode node, Dictionary<string, SnapshotElementDto> elements)
    {
        if (node.IsEmpty)
        {
            return;
        }

        elements[node.Ref] = ToElementDto(node);
        foreach (var child in node.Children)
        {
            CollectElements(child, elements);
        }
    }

    private static SnapshotElementDto ToElementDto(SnapshotNode node) => new()
    {
        Ref = node.Ref,
        Role = node.Role,
        Name = string.IsNullOrEmpty(node.Name) ? null : node.Name,
        Value = node.Value,
        States = node.States.Count == 0 ? null : node.States,
        Bounds = node.Bounds,
        Center = node.Center,
        Actions = node.Actions.Count == 0 ? null : node.Actions,
    };

    private static string SerializeYaml(SnapshotNode root)
    {
        var sb = new StringBuilder();
        WriteYamlNode(sb, root, indent: 0);
        return sb.ToString().TrimEnd();
    }

    private static void WriteYamlNode(StringBuilder sb, SnapshotNode node, int indent)
    {
        if (node.IsEmpty)
        {
            return;
        }

        var pad = new string(' ', indent);
        sb.Append(pad);
        sb.Append("- ");
        sb.Append(node.Role);

        if (!string.IsNullOrEmpty(node.Name))
        {
            sb.Append(' ');
            sb.Append(QuoteYaml(node.Name));
        }

        foreach (var state in node.States)
        {
            if (state.Contains('=', StringComparison.Ordinal))
            {
                var parts = state.Split('=', 2);
                sb.Append(" [");
                sb.Append(parts[0]);
                sb.Append('=');
                sb.Append(parts[1]);
                sb.Append(']');
            }
            else
            {
                sb.Append(" [");
                sb.Append(state);
                sb.Append(']');
            }
        }

        sb.Append(" [ref=");
        sb.Append(node.Ref);
        sb.Append(']');

        var hasChildren = node.Children.Count > 0;
        var hasValue = !string.IsNullOrEmpty(node.Value);

        if (hasValue && !hasChildren)
        {
            sb.Append(": ");
            sb.Append(node.Value);
            sb.AppendLine();
            return;
        }

        if (hasValue || hasChildren)
        {
            sb.Append(':');
        }

        sb.AppendLine();

        if (hasValue)
        {
            sb.Append(pad);
            sb.Append("    ");
            sb.Append(node.Value);
            sb.AppendLine();
        }

        foreach (var child in node.Children)
        {
            WriteYamlNode(sb, child, indent + 2);
        }
    }

    private static string QuoteYaml(string value)
    {
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && sb.Length > 0)
            {
                sb.Append(' ');
            }

            pendingSpace = false;
            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..Math.Max(0, maxLength - 1)] + "…";
    }

    private sealed class SnapshotNode
    {
        public static SnapshotNode Empty { get; } = new()
        {
            Ref = string.Empty,
            ControlTypeId = -1,
            Role = string.Empty,
            Name = string.Empty,
            Value = null,
            States = [],
            Actions = [],
            Bounds = new RectDto { X = 0, Y = 0, Width = 0, Height = 0 },
            Center = new PointDto { X = 0, Y = 0 },
            Children = [],
            IsEmpty = true,
        };

        public required string Ref { get; init; }
        public required int ControlTypeId { get; init; }
        public required string Role { get; init; }
        public required string Name { get; init; }
        public string? Value { get; init; }
        public required List<string> States { get; init; }
        public required List<string> Actions { get; init; }
        public required RectDto Bounds { get; init; }
        public required PointDto Center { get; init; }
        public required List<SnapshotNode> Children { get; set; }
        public bool IsEmpty { get; init; }
    }
}
