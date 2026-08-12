using System.Text.Json.Serialization;

namespace ComputerUse.Cli.Json;

internal sealed class CliResponse
{
    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PointDto? Cursor { get; init; }

    [JsonPropertyName("screen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreenInfoDto? Screen { get; init; }

    [JsonPropertyName("screenshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ScreenshotDto? Screenshot { get; init; }

    [JsonPropertyName("windows")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WindowDto>? Windows { get; init; }

    [JsonPropertyName("uia")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UiaDumpDto? Uia { get; init; }

    [JsonPropertyName("snapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snapshot { get; init; }

    [JsonPropertyName("elements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, SnapshotElementDto>? Elements { get; init; }

    [JsonPropertyName("element")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SnapshotElementDto? Element { get; init; }

    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SnapshotMetaDto? Meta { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorDto? Error { get; init; }
}

internal sealed class PointDto
{
    [JsonPropertyName("x")]
    public required int X { get; init; }

    [JsonPropertyName("y")]
    public required int Y { get; init; }
}

internal sealed class RectDto
{
    [JsonPropertyName("x")]
    public required int X { get; init; }

    [JsonPropertyName("y")]
    public required int Y { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

internal sealed class MonitorDto
{
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("primary")]
    public required bool Primary { get; init; }

    [JsonPropertyName("bounds")]
    public required RectDto Bounds { get; init; }

    [JsonPropertyName("workArea")]
    public required RectDto WorkArea { get; init; }
}

internal sealed class ScreenInfoDto
{
    [JsonPropertyName("virtualBounds")]
    public required RectDto VirtualBounds { get; init; }

    [JsonPropertyName("monitors")]
    public required IReadOnlyList<MonitorDto> Monitors { get; init; }
}

internal sealed class ScreenshotDto
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Screen-space source rectangle that was captured (maps image pixels back to desktop).</summary>
    [JsonPropertyName("region")]
    public required RectDto Region { get; init; }
}

internal sealed class WindowDto
{
    [JsonPropertyName("hwnd")]
    public required string Hwnd { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("className")]
    public required string ClassName { get; init; }

    [JsonPropertyName("processId")]
    public required int ProcessId { get; init; }

    [JsonPropertyName("processName")]
    public required string ProcessName { get; init; }

    [JsonPropertyName("bounds")]
    public required RectDto Bounds { get; init; }

    [JsonPropertyName("foreground")]
    public required bool Foreground { get; init; }

    [JsonPropertyName("visible")]
    public required bool Visible { get; init; }

    [JsonPropertyName("minimized")]
    public required bool Minimized { get; init; }
}

internal sealed class UiaDumpDto
{
    [JsonPropertyName("hwnd")]
    public required string Hwnd { get; init; }

    [JsonPropertyName("depth")]
    public required int Depth { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("root")]
    public required UiaNodeDto Root { get; init; }
}

internal sealed class UiaNodeDto
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("controlType")]
    public required string ControlType { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("automationId")]
    public required string AutomationId { get; init; }

    [JsonPropertyName("className")]
    public required string ClassName { get; init; }

    [JsonPropertyName("bounds")]
    public required RectDto Bounds { get; init; }

    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    [JsonPropertyName("offscreen")]
    public required bool Offscreen { get; init; }

    [JsonPropertyName("children")]
    public required IReadOnlyList<UiaNodeDto> Children { get; set; }
}

internal sealed class ErrorDto
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("help")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Help { get; init; }
}

internal sealed class WindowSnapshotDto
{
    [JsonPropertyName("snapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snapshot { get; init; }

    [JsonPropertyName("elements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, SnapshotElementDto>? Elements { get; init; }

    [JsonPropertyName("meta")]
    public required SnapshotMetaDto Meta { get; init; }
}

internal sealed class SnapshotMetaDto
{
    [JsonPropertyName("hwnd")]
    public required string Hwnd { get; init; }

    [JsonPropertyName("view")]
    public required string View { get; init; }

    [JsonPropertyName("interactiveOnly")]
    public required bool InteractiveOnly { get; init; }

    [JsonPropertyName("depth")]
    public required int Depth { get; init; }

    [JsonPropertyName("nodeCount")]
    public required int NodeCount { get; init; }

    [JsonPropertyName("truncated")]
    public required bool Truncated { get; init; }

    [JsonPropertyName("format")]
    public required string Format { get; init; }
}

internal sealed class SnapshotElementDto
{
    [JsonPropertyName("ref")]
    public required string Ref { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; init; }

    [JsonPropertyName("states")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? States { get; init; }

    [JsonPropertyName("bounds")]
    public required RectDto Bounds { get; init; }

    [JsonPropertyName("center")]
    public required PointDto Center { get; init; }

    [JsonPropertyName("actions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Actions { get; init; }
}
