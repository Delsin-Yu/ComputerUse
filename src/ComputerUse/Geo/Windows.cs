using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ComputerUse.Cli;

namespace ComputerUse.Geo;

/// <summary>
/// A top-level window. <c>Hwnd</c> is the handle as printed (<c>0x00040C1A</c>), <c>Rect</c> is the
/// DWM extended frame in physical desktop pixels, and <c>Process</c> is the image name without its
/// extension (empty when the process cannot be queried).
/// </summary>
internal sealed record WindowInfo(
    nint Handle,
    string Hwnd,
    string Title,
    string Class,
    int ProcessId,
    string Process,
    RectI Rect,
    bool Foreground,
    bool Minimized,
    bool Visible,
    bool Enabled,
    nint Owner);

/// <summary>
/// Enumeration, lookup and focus of top-level windows. Named <c>WindowList</c> rather than
/// <c>Windows</c> so it never reads like <c>System.Windows</c> or a Win32 type. Every rectangle is a
/// physical pixel rectangle in the virtual-screen coordinate space.
/// </summary>
internal static class WindowList
{
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const uint WsExToolWindow = 0x0000_0080;
    private const uint GaRoot = 2;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int MaxTitle = 512;
    private const int MaxClass = 256;
    private const int ProcessNameCapacity = 1024;
    private const int RestoreBudgetMs = 400;
    private const int MaxCandidates = 10;

    private static readonly string[] SelectorHints =
    [
        "hwnd:<handle> selects a window by handle (0x-hex or decimal)",
        "title:<glob>, process:<glob> and class:<glob> match case-insensitive shell globs (* and ?)",
    ];

    private static readonly string[] AmbiguousHints =
    [
        "pass --index N to pick one of the listed candidates",
        "or narrow the selector so that exactly one window matches",
    ];

    private enum SelectorKind
    {
        Handle,
        Title,
        Process,
        Class,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>State for the EnumWindows callback, which cannot capture anything.</summary>
    private sealed class EnumContext
    {
        public List<nint> Handles { get; } = new(64);

        public bool IncludeInvisible { get; init; }
    }

    [ThreadStatic]
    private static EnumContext? _context;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern unsafe bool EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern unsafe int GetWindowTextW(nint hwnd, char* text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern unsafe int GetClassNameW(nint hwnd, char* name, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern unsafe bool QueryFullProcessImageNameW(nint process, int flags, char* name, ref int size);

    /// <summary>
    /// Top-level windows, foreground first and then in z-order. Owned windows and
    /// <c>WS_EX_TOOLWINDOW</c> windows are always skipped: they are never what an agent meant.
    /// </summary>
    public static IReadOnlyList<WindowInfo> List(bool includeInvisible = false, bool includeEmptyTitle = false)
    {
        var context = new EnumContext { IncludeInvisible = includeInvisible };
        _context = context;
        try
        {
            unsafe
            {
                _ = EnumWindows(&WindowEnumerator, 0);
            }
        }
        finally
        {
            _context = null;
        }

        var foreground = GetForegroundWindow();
        var processes = new Dictionary<int, string>(context.Handles.Count);
        var windows = new List<WindowInfo>(context.Handles.Count);

        foreach (var hwnd in context.Handles)
        {
            var info = TryDescribe(hwnd, foreground, processes, includeEmptyTitle);
            if (info is not null)
            {
                windows.Add(info);
            }
        }

        var foregroundIndex = windows.FindIndex(w => w.Handle == foreground);
        if (foregroundIndex > 0)
        {
            var first = windows[foregroundIndex];
            windows.RemoveAt(foregroundIndex);
            windows.Insert(0, first);
        }

        return windows;
    }

    /// <summary>One window by handle; throws <c>InvalidWindow</c> (exit 2) when it is not a window.</summary>
    public static WindowInfo Get(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            throw InvalidWindow(hwnd);
        }

        return TryDescribe(hwnd, GetForegroundWindow(), new Dictionary<int, string>(1), includeEmptyTitle: true)
            ?? throw new CliError(
                "InvalidWindow",
                $"Window {FormatHwnd(hwnd)} exists but could not be read; it may have been destroyed.",
                2,
                new JsonObject { ["hwnd"] = FormatHwnd(hwnd) },
                ["list the open windows to get a current handle"]);
    }

    /// <summary>The canonical textual form of a handle: <c>0x00040C1A</c> (uppercase, at least 8 digits).</summary>
    public static string FormatHwnd(nint hwnd) => "0x" + ((ulong)(long)hwnd).ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>
    /// A handle from <c>0x</c>-prefixed hex or a decimal number (a leading <c>0x</c>-less run of hex
    /// digits is also accepted); throws <c>InvalidArgument</c> (exit 1) when it is neither.
    /// </summary>
    public static nint ParseHwnd(string text)
    {
        var value = (text ?? "").Trim();
        if (value.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[5..].Trim();
        }

        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var digits = hex ? value[2..].Trim() : value;
        if (digits.Length == 0)
        {
            throw InvalidArgument($"'{text}' is not a window handle; use 0x-hex or a decimal number.");
        }

        ulong handle;
        var parsed = hex
            ? ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle)
            : ulong.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out handle)
                || ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle);

        if (!parsed || handle == 0 || handle > long.MaxValue)
        {
            throw InvalidArgument($"'{text}' is not a window handle; use 0x-hex or a decimal number.");
        }

        return (nint)unchecked((long)handle);
    }

    /// <summary>
    /// The DWM extended frame bounds in physical pixels (the visible frame, not the shadow-inflated
    /// <c>GetWindowRect</c>), falling back to <c>GetWindowRect</c> when DWM has no opinion.
    /// </summary>
    public static RectI FrameBounds(nint hwnd)
    {
        if (hwnd == 0)
        {
            return default;
        }

        if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var dwm, Marshal.SizeOf<RECT>()) == 0)
        {
            var extended = ToRect(dwm);
            if (!extended.IsEmpty)
            {
                return extended;
            }
        }

        return GetWindowRect(hwnd, out var rect) ? ToRect(rect) : default;
    }

    /// <summary>
    /// A single window from a selector: <c>hwnd:0x00120AB4</c>, a bare handle, or
    /// <c>title:</c>/<c>process:</c>/<c>class:</c> plus a case-insensitive shell glob. Throws
    /// <c>InvalidArgument</c> (exit 1) for a malformed selector, <c>NotFound</c>/<c>Ambiguous</c>
    /// (exit 2) for zero/several matches, and <c>InvalidWindow</c> for a handle that is not a window.
    /// </summary>
    public static WindowInfo Resolve(string selector, int? index = null)
    {
        var text = (selector ?? "").Trim();
        if (text.Length == 0)
        {
            throw new CliError(
                "InvalidArgument",
                "A window selector is required: hwnd:<handle>, title:<glob>, process:<glob> or class:<glob>.",
                1,
                hints: SelectorHints);
        }

        var kind = SelectorKind.Handle;
        var hasPrefix = false;
        var rest = text;

        var colon = text.IndexOf(':');
        if (colon >= 0)
        {
            var prefix = text[..colon].Trim();
            switch (prefix.ToLowerInvariant())
            {
                case "hwnd":
                    kind = SelectorKind.Handle;
                    hasPrefix = true;
                    break;
                case "title":
                    kind = SelectorKind.Title;
                    hasPrefix = true;
                    break;
                case "process":
                    kind = SelectorKind.Process;
                    hasPrefix = true;
                    break;
                case "class":
                    kind = SelectorKind.Class;
                    hasPrefix = true;
                    break;
                default:
                    throw new CliError(
                        "InvalidArgument",
                        $"Unknown window selector prefix '{prefix}:' (expected hwnd:, title:, process: or class:).",
                        1,
                        new JsonObject { ["selector"] = text },
                        SelectorHints);
            }

            rest = text[(colon + 1)..];
        }

        if (!hasPrefix)
        {
            if (!LooksLikeHandle(text))
            {
                throw new CliError(
                    "InvalidArgument",
                    $"'{text}' is neither a window handle nor a prefixed selector (hwnd:, title:, process:, class:).",
                    1,
                    new JsonObject { ["selector"] = text },
                    SelectorHints);
            }

            return Get(ParseHwnd(text));
        }

        if (kind == SelectorKind.Handle)
        {
            return Get(ParseHwnd(rest.Trim()));
        }

        var pattern = rest.Trim();
        if (pattern.Length == 0)
        {
            throw new CliError(
                "InvalidArgument",
                "The selector pattern is empty; give a glob such as 'title:Untitled*'.",
                1,
                new JsonObject { ["selector"] = text },
                SelectorHints);
        }

        // Visible titled windows are the intended target set; untitled or hidden windows are only
        // searched when that set yields nothing, so helper windows do not make every match ambiguous.
        var pool = List();
        var matches = Match(kind, pattern, pool);

        if (matches.Count == 0)
        {
            var everything = List(includeInvisible: true, includeEmptyTitle: true);
            if (everything.Count != pool.Count)
            {
                var wider = Match(kind, pattern, everything);
                if (wider.Count > 0)
                {
                    matches = wider;
                    pool = everything;
                }
            }
        }

        if (matches.Count == 0)
        {
            throw new CliError(
                "NotFound",
                $"No open window matches '{text}'.",
                2,
                SelectorContext(text, pool, 0),
                [
                    "list the open windows to see the titles, processes and classes that exist",
                    "globs are case-insensitive and support * and ?",
                ]);
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (index is null)
        {
            throw new CliError(
                "Ambiguous",
                $"{matches.Count} windows match '{text}'; one window is required.",
                2,
                SelectorContext(text, matches, matches.Count),
                AmbiguousHints);
        }

        if (index.Value < 0 || index.Value >= matches.Count)
        {
            throw new CliError(
                "InvalidArgument",
                $"--index {index.Value} is out of range: '{text}' matched {matches.Count} windows (0-{matches.Count - 1}).",
                1,
                SelectorContext(text, matches, matches.Count));
        }

        return matches[index.Value];
    }

    /// <summary>
    /// Makes the window the foreground window. A minimized window is restored first and its
    /// rectangle is re-read afterwards, because restoring or maximizing changes it. Returns the fresh
    /// state of the window, so a caller can prove the focus happened.
    /// </summary>
    public static WindowInfo Focus(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            throw InvalidWindow(hwnd);
        }

        if (IsIconic(hwnd))
        {
            RestoreAndSettle(hwnd);
        }
        else if (!IsWindowVisible(hwnd))
        {
            _ = ShowWindow(hwnd, SwShow);
        }

        for (var attempt = 0; attempt < 3 && GetForegroundWindow() != hwnd; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(20);
            }

            Nudge(hwnd);
        }

        if (GetForegroundWindow() != hwnd)
        {
            var foreground = GetForegroundWindow();
            var context = new JsonObject
            {
                ["hwnd"] = FormatHwnd(hwnd),
                ["foreground"] = foreground == 0 ? null : FormatHwnd(foreground),
                ["foregroundTitle"] = foreground == 0 ? "" : Title(foreground),
            };

            throw new CliError(
                "FocusRefused",
                $"Windows refused to make {FormatHwnd(hwnd)} the foreground window (another window kept the foreground).",
                2,
                context,
                [
                    "the foreground lock only lets a window be raised when the caller is in the foreground",
                    "a full-screen or always-on-top window may be covering it",
                ]);
        }

        return Get(hwnd);
    }

    /// <summary>
    /// The top-level window under the point and the class of the raw child that was hit (empty when
    /// nothing is there). The returned rectangle is the ROOT window's frame.
    /// </summary>
    public static WindowInfo? FromPoint(PointI p, out string hitClass)
    {
        hitClass = "";

        var hit = WindowFromPoint(new POINT { X = p.X, Y = p.Y });
        if (hit == 0)
        {
            return null;
        }

        hitClass = Class(hit);

        var root = GetAncestor(hit, GaRoot);
        if (root == 0)
        {
            root = hit;
        }

        if (!IsWindow(root))
        {
            return null;
        }

        return TryDescribe(root, GetForegroundWindow(), new Dictionary<int, string>(1), includeEmptyTitle: true);
    }

    [UnmanagedCallersOnly]
    private static int WindowEnumerator(nint hwnd, nint lParam)
    {
        var context = _context;
        if (context is null)
        {
            return 0;
        }

        try
        {
            if (IsCandidate(hwnd, context.IncludeInvisible))
            {
                context.Handles.Add(hwnd);
            }
        }
        catch
        {
            // A window that cannot be inspected is skipped: the callback must never throw.
        }

        return 1;
    }

    private static bool IsCandidate(nint hwnd, bool includeInvisible)
    {
        if (hwnd == 0)
        {
            return false;
        }

        // Owned windows used to be skipped as noise, which hid the one window that matters most:
        // a modal file dialog is owned by the window that opened it (measured: Notepad's Open
        // dialog is #32770, owned by the Notepad window, reported by a different process). Tool
        // windows are still skipped, which keeps palettes and trays out.
        if (((uint)GetWindowLongW(hwnd, GwlExStyle) & WsExToolWindow) != 0)
        {
            return false;
        }

        // Helper windows that exist only to receive messages are never a target an agent meant.
        if (IsHelperClass(Class(hwnd)))
        {
            return false;
        }

        return includeInvisible || IsWindowVisible(hwnd);
    }

    private static readonly string[] HelperClasses =
    [
        "GDI+ Hook Window Class",
        "MSCTFIME UI",
        "IME",
        "Chrome_SystemMessageWindow",
        "GhostWindow",
        "TaskListThumbnailWnd",
        "SysShadow",
        "Windows.UI.Core.CoreWindow",
        "ApplicationManager_DesktopShellWindow",
    ];

    /// <summary>
    /// The visible window that a modal dialog opened on top of <paramref name="hwnd"/> would own,
    /// i.e. the dialog that makes the target stop responding to clicks. Null when there is none.
    /// </summary>
    public static WindowInfo? ModalBlocker(nint hwnd)
    {
        foreach (var candidate in List(includeInvisible: false))
        {
            if (candidate.Owner == hwnd && candidate.Enabled && candidate.Handle != hwnd)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsHelperClass(string className)
    {
        foreach (var helper in HelperClasses)
        {
            if (string.Equals(className, helper, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static WindowInfo? TryDescribe(
        nint hwnd,
        nint foreground,
        Dictionary<int, string> processes,
        bool includeEmptyTitle)
    {
        try
        {
            var title = Title(hwnd);
            if (!includeEmptyTitle && title.Length == 0)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(hwnd, out var processId);

            return new WindowInfo(
                hwnd,
                FormatHwnd(hwnd),
                title,
                Class(hwnd),
                processId,
                ProcessName(processId, processes),
                FrameBounds(hwnd),
                hwnd == foreground,
                IsIconic(hwnd),
                IsWindowVisible(hwnd),
                IsWindowEnabled(hwnd),
                GetWindow(hwnd, GwOwner));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The window title, capped at 512 characters, or an empty string for a hung window: reading the
    /// title of a wedged foreign window must never stall the command.
    /// </summary>
    private static unsafe string Title(nint hwnd)
    {
        if (IsHungAppWindow(hwnd))
        {
            return "";
        }

        const int Capacity = MaxTitle + 1;
        char* buffer = stackalloc char[Capacity];
        var length = GetWindowTextW(hwnd, buffer, Capacity);
        if (length <= 0)
        {
            return "";
        }

        return new string(buffer, 0, Math.Min(length, MaxTitle));
    }

    private static unsafe string Class(nint hwnd)
    {
        char* buffer = stackalloc char[MaxClass];
        var length = GetClassNameW(hwnd, buffer, MaxClass);
        return length <= 0 ? "" : new string(buffer, 0, Math.Min(length, MaxClass - 1));
    }

    /// <summary>Image name without extension; empty when the process cannot be opened.</summary>
    private static unsafe string ProcessName(int processId)
    {
        if (processId <= 0)
        {
            return "";
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return "";
        }

        try
        {
            char* buffer = stackalloc char[ProcessNameCapacity];
            var size = ProcessNameCapacity;
            if (!QueryFullProcessImageNameW(process, 0, buffer, ref size))
            {
                return "";
            }

            var length = Math.Min(size, ProcessNameCapacity);
            while (length > 0 && buffer[length - 1] == '\0')
            {
                length--;
            }

            if (length == 0)
            {
                return "";
            }

            return Path.GetFileNameWithoutExtension(new string(buffer, 0, length)) ?? "";
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    private static string ProcessName(int processId, Dictionary<int, string> cache)
    {
        if (processId <= 0)
        {
            return "";
        }

        if (cache.TryGetValue(processId, out var cached))
        {
            return cached;
        }

        var name = ProcessName(processId);
        cache[processId] = name;
        return name;
    }

    /// <summary>Restore a minimized window and wait (at most 400 ms) for its rectangle to settle.</summary>
    private static void RestoreAndSettle(nint hwnd)
    {
        _ = ShowWindow(hwnd, SwRestore);

        var deadline = Environment.TickCount64 + RestoreBudgetMs;
        var previous = FrameBounds(hwnd);

        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(15);

            var current = FrameBounds(hwnd);
            if (!IsIconic(hwnd) && current == previous)
            {
                return;
            }

            previous = current;
        }
    }

    /// <summary>
    /// The foreground-window dance: attach to the thread that currently owns the foreground so this
    /// thread is allowed to steal it, raise the window, then detach.
    /// </summary>
    private static void Nudge(nint hwnd)
    {
        var currentThread = GetCurrentThreadId();
        var foreground = GetForegroundWindow();

        var foregroundThread = foreground != 0 && foreground != hwnd
            ? GetWindowThreadProcessId(foreground, out _)
            : 0u;

        var attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(foregroundThread, currentThread, true);

        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(foregroundThread, currentThread, false);
            }
        }
    }

    /// <summary>Windows matching the glob on the selected attribute.</summary>
    private static List<WindowInfo> Match(SelectorKind kind, string pattern, IReadOnlyList<WindowInfo> pool)
    {
        var matches = new List<WindowInfo>();
        foreach (var window in pool)
        {
            var value = kind switch
            {
                SelectorKind.Title => window.Title,
                SelectorKind.Process => window.Process,
                SelectorKind.Class => window.Class,
                _ => "",
            };

            if (Glob(pattern, value))
            {
                matches.Add(window);
            }
        }

        return matches;
    }

    /// <summary>Case-insensitive shell glob supporting <c>*</c> and <c>?</c>.</summary>
    private static bool Glob(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, mark = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || SameChar(pattern[p], value[v])))
            {
                p++;
                v++;
                continue;
            }

            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = v;
                continue;
            }

            if (star >= 0)
            {
                p = star + 1;
                v = ++mark;
                continue;
            }

            return false;
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool SameChar(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    /// <summary>A bare token counts as a handle when it is all decimal digits or <c>0x</c>-prefixed hex.</summary>
    private static bool LooksLikeHandle(string text)
    {
        var value = text.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var digits = value[2..];
            if (digits.Length == 0)
            {
                return false;
            }

            foreach (var c in digits)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        if (value.Length == 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static JsonObject SelectorContext(string selector, IReadOnlyList<WindowInfo> candidates, int total)
    {
        var array = new JsonArray();
        for (var i = 0; i < candidates.Count && i < MaxCandidates; i++)
        {
            var candidate = candidates[i];
            JsonNode node = new JsonObject
            {
                ["hwnd"] = candidate.Hwnd,
                ["title"] = candidate.Title,
                ["process"] = candidate.Process,
            };
            array.Add(node);
        }

        return new JsonObject
        {
            ["selector"] = selector,
            ["matchCount"] = total,
            ["candidates"] = array,
        };
    }

    private static RectI ToRect(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static CliError InvalidWindow(nint hwnd) => new(
        "InvalidWindow",
        $"Handle {FormatHwnd(hwnd)} is not an open window.",
        2,
        new JsonObject { ["hwnd"] = FormatHwnd(hwnd) },
        ["list the open windows to get a current handle"]);

    private static CliError InvalidArgument(string message) =>
        new("InvalidArgument", message, 1);
}
