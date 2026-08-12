using System.Text.Encodings.Web;
using System.Text.Json;
using ComputerUse.Cli.Win32;

namespace ComputerUse.Cli.Json;

internal static class ResponseWriter
{
    private static readonly CliJsonContext Json = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static int Success(string command, Action<SuccessBuilder>? configure = null)
    {
        var builder = new SuccessBuilder(command);
        configure?.Invoke(builder);
        Write(builder.Build());
        return 0;
    }

    public static int Fail(string command, Exception ex)
    {
        if (ex is CliException cli)
        {
            Write(new CliResponse
            {
                Ok = false,
                Command = command,
                Error = new ErrorDto { Code = cli.Code, Message = cli.Message },
            });
            return cli.ExitCode;
        }

        Write(new CliResponse
        {
            Ok = false,
            Command = command,
            Error = new ErrorDto { Code = "RuntimeError", Message = ex.Message },
        });
        return 2;
    }

    public static int FailParse(string command, string message, string? help = null)
    {
        Write(new CliResponse
        {
            Ok = false,
            Command = command,
            Error = new ErrorDto
            {
                Code = "InvalidArgument",
                Message = message,
                Help = help,
            },
        });
        return 1;
    }

    private static void Write(CliResponse response)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(response, Json.CliResponse));
    }

    internal sealed class SuccessBuilder(string command)
    {
        private PointDto? _cursor;
        private ScreenInfoDto? _screen;
        private ScreenshotDto? _screenshot;
        private IReadOnlyList<WindowDto>? _windows;
        private UiaDumpDto? _uia;
        private string? _snapshot;
        private Dictionary<string, SnapshotElementDto>? _elements;
        private SnapshotElementDto? _element;
        private SnapshotMetaDto? _meta;
        private bool _includeCursor = true;

        public SuccessBuilder WithCursor(PointDto cursor)
        {
            _cursor = cursor;
            return this;
        }

        public SuccessBuilder WithCurrentCursor()
        {
            var p = CursorService.GetPosition();
            _cursor = new PointDto { X = p.X, Y = p.Y };
            return this;
        }

        public SuccessBuilder WithoutCursor()
        {
            _includeCursor = false;
            _cursor = null;
            return this;
        }

        public SuccessBuilder WithScreen(ScreenInfoDto screen)
        {
            _screen = screen;
            return this;
        }

        public SuccessBuilder WithScreenshot(ScreenshotDto screenshot)
        {
            _screenshot = screenshot;
            return this;
        }

        public SuccessBuilder WithWindows(IReadOnlyList<WindowDto> windows)
        {
            _windows = windows;
            return this;
        }

        public SuccessBuilder WithUia(UiaDumpDto uia)
        {
            _uia = uia;
            return this;
        }

        public SuccessBuilder WithWindowSnapshot(WindowSnapshotDto snapshot)
        {
            _snapshot = snapshot.Snapshot;
            _elements = snapshot.Elements;
            _meta = snapshot.Meta;
            return this;
        }

        public SuccessBuilder WithElement(SnapshotElementDto element)
        {
            _element = element;
            return this;
        }

        public CliResponse Build()
        {
            if (_includeCursor && _cursor is null)
            {
                WithCurrentCursor();
            }

            return new CliResponse
            {
                Ok = true,
                Command = command,
                Cursor = _cursor,
                Screen = _screen,
                Screenshot = _screenshot,
                Windows = _windows,
                Uia = _uia,
                Snapshot = _snapshot,
                Elements = _elements,
                Element = _element,
                Meta = _meta,
            };
        }
    }
}
