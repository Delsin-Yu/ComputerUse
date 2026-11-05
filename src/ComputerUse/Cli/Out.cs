using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComputerUse.Geo;

namespace ComputerUse.Cli;

/// <summary>JSON envelope: one object on stdout, nothing else.</summary>
internal static class Out
{
    // ASCII-only JSON: every non-ASCII character is escaped as a JSON unicode escape, so a consumer
    // that decodes stdout with the wrong code page still sees valid JSON (titles are full of CJK).
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Default,
    };

    public static JsonObject Point(PointI p) => new() { ["x"] = p.X, ["y"] = p.Y };

    public static JsonObject Rect(RectI r) => new()
    {
        ["x"] = r.X,
        ["y"] = r.Y,
        ["width"] = r.W,
        ["height"] = r.H,
    };

    public static JsonObject Window(WindowInfo w) => new()
    {
        ["hwnd"] = w.Hwnd,
        ["title"] = w.Title,
        ["class"] = w.Class,
        ["processId"] = w.ProcessId,
        ["process"] = w.Process,
        ["rect"] = Rect(w.Rect),
        ["foreground"] = w.Foreground,
        ["minimized"] = w.Minimized,
        ["visible"] = w.Visible,
    };

    public static JsonObject Monitor(MonitorInfo m) => new()
    {
        ["index"] = m.Index,
        ["device"] = m.Device,
        ["primary"] = m.Primary,
        ["rect"] = Rect(m.Rect),
        ["workArea"] = Rect(m.WorkArea),
        ["dpi"] = m.Dpi,
        ["scale"] = m.ScalePercent,
    };

    public static JsonArray Strings(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add((System.Text.Json.Nodes.JsonNode)value);
        }

        return array;
    }

    public static int Emit(JsonObject response)
    {
        Console.Out.Write(response.ToJsonString(Options));
        Console.Out.Write('\n');
        Console.Out.Flush();
        return 0;
    }

    public static int Fail(string command, CliError error)
    {
        var payload = new JsonObject
        {
            ["ok"] = false,
            ["command"] = command,
            ["error"] = new JsonObject
            {
                ["code"] = error.Code,
                ["message"] = error.Message,
            },
        };

        var body = payload["error"]!.AsObject();

        if (error.Context is { Count: > 0 })
        {
            body["context"] = error.Context.DeepClone();
        }

        if (error.Hints is { Length: > 0 })
        {
            body["hints"] = Strings(error.Hints);
        }

        Emit(payload);
        return error.ExitCode;
    }

    public static int Fail(
        string command,
        string code,
        string message,
        int exitCode = 2,
        JsonObject? context = null,
        string[]? hints = null)
    {
        return Fail(command, new CliError(code, message, exitCode, context, hints));
    }

    public static int FailUsage(string command, UsageException error)
    {
        var payload = new JsonObject
        {
            ["ok"] = false,
            ["command"] = command,
            ["error"] = new JsonObject
            {
                ["code"] = "InvalidArgument",
                ["message"] = error.Message,
            },
        };

        if (error.Help is not null)
        {
            payload["error"]!["help"] = error.Help;
        }

        Emit(payload);
        return 1;
    }
}

/// <summary>Builder for a success response; stamps elapsed milliseconds on emit.</summary>
internal sealed class Response
{
    private readonly JsonObject _root;
    private readonly long _startTicks;
    private readonly List<string> _warnings = new();

    public Response(string command)
    {
        _root = new JsonObject
        {
            ["ok"] = true,
            ["command"] = command,
        };

        _startTicks = Environment.TickCount64;
    }

    public JsonObject Root => _root;

    public Response Set(string key, JsonNode? value)
    {
        _root[key] = value;
        return this;
    }

    public Response Pointer(PointI? p)
    {
        if (p is { } point)
        {
            _root["pointer"] = new JsonObject
            {
                ["x"] = point.X,
                ["y"] = point.Y,
                ["monitor"] = Monitors.At(point)?.Index,
            };
        }

        return this;
    }

    public Response Warn(string message)
    {
        _warnings.Add(message);
        return this;
    }

    public int Emit()
    {
        if (_warnings.Count > 0)
        {
            _root["warnings"] = Out.Strings(_warnings);
        }

        _root["ms"] = Environment.TickCount64 - _startTicks;
        return Out.Emit(_root);
    }
}
