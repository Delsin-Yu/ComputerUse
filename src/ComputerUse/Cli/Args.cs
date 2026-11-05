using System.Globalization;
using ComputerUse.Geo;

namespace ComputerUse.Cli;

/// <summary>
/// Minimal hand-written option parser. Grammar: <c>--name value</c>, <c>--name=value</c>,
/// <c>--flag</c>, repeatable options, trailing positionals. Coordinates are always a single
/// comma token (<c>--at 1200,-40</c>) so a bare negative number never has to be guessed at.
/// </summary>
internal sealed class Args
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _present = new(StringComparer.Ordinal);
    private readonly List<string> _positionals = new();

    private Args(string command, string help)
    {
        Command = command;
        Help = help;
    }

    public string Command { get; }

    public string Help { get; }

    public IReadOnlyList<string> Positionals => _positionals;

    public static Args Parse(
        string[] argv,
        int start,
        string command,
        string help,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> flagOptions,
        IReadOnlySet<string>? optionalValueOptions = null)
    {
        var args = new Args(command, help);

        for (var i = start; i < argv.Length; i++)
        {
            var token = argv[i];

            if (token.Length > 2 && token.StartsWith("--", StringComparison.Ordinal))
            {
                var name = token[2..];
                string? inline = null;

                var eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    inline = name[(eq + 1)..];
                    name = name[..eq];
                }

                var key = "--" + name;

                if (flagOptions.Contains(key))
                {
                    if (inline is not null)
                    {
                        throw new UsageException($"{key} is a flag and takes no value.", help);
                    }

                    args._flags.Add(key);
                    args._present.Add(key);
                    continue;
                }

                var optional = optionalValueOptions is not null && optionalValueOptions.Contains(key);

                if (!optional && !valueOptions.Contains(key))
                {
                    throw new UsageException($"Unknown option {key} for '{command}'.", help);
                }

                string? value = null;
                if (inline is not null)
                {
                    value = inline;
                }
                else if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = argv[++i];
                }
                else if (!optional)
                {
                    throw new UsageException($"Option {key} requires a value.", help);
                }

                args._present.Add(key);

                if (value is null)
                {
                    continue;
                }

                if (!args._values.TryGetValue(key, out var list))
                {
                    args._values[key] = list = new List<string>(1);
                }

                list.Add(value);
                continue;
            }

            if (token.Length > 1 && token[0] == '-')
            {
                throw new UsageException($"Unknown option '{token}' (options are spelled --name).", help);
            }

            args._positionals.Add(token);
        }

        return args;
    }

    public bool Has(string name) => _present.Contains(name) || _values.ContainsKey(name);

    public bool Flag(string name) => _flags.Contains(name) || (_present.Contains(name) && !_values.ContainsKey(name));

    /// <summary>Value of an option that may appear with or without a value (null when absent).</summary>
    public string? Optional(string name) => _present.Contains(name) ? Get(name) : null;

    public IReadOnlyList<string> All(string name) =>
        _values.TryGetValue(name, out var list) ? list : Array.Empty<string>();

    /// <summary>Single-valued option; repeating it is a usage error.</summary>
    public string? Get(string name)
    {
        var all = All(name);
        if (all.Count > 1)
        {
            throw new UsageException($"Option {name} may only be given once.", Help);
        }

        return all.Count == 1 ? all[0] : null;
    }

    public string Require(string name)
    {
        return Get(name) ?? throw new UsageException($"Option {name} is required.", Help);
    }

    public int? Int(string name)
    {
        var text = Get(name);
        if (text is null)
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw new UsageException($"Option {name} expects an integer, got '{text}'.", Help);
        }

        return value;
    }

    public int IntOr(string name, int fallback) => Int(name) ?? fallback;

    public int RequireInt(string name)
    {
        return Int(name) ?? throw new UsageException($"Option {name} is required.", Help);
    }

    /// <summary>Comma pair in the command's coordinate space.</summary>
    public PointI? Point(string name)
    {
        var text = Get(name);
        return text is null ? null : ParsePoint(name, text);
    }

    public PointI RequirePoint(string name)
    {
        return Point(name) ?? throw new UsageException($"Option {name} is required.", Help);
    }

    /// <summary>Comma quad (x,y,w,h) in the command's coordinate space.</summary>
    public RectI? Rect(string name)
    {
        var text = Get(name);
        if (text is null)
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            throw new UsageException($"Option {name} expects x,y,w,h (got '{text}').", Help);
        }

        var rect = new RectI(
            ParseInt(name, parts[0]),
            ParseInt(name, parts[1]),
            ParseInt(name, parts[2]),
            ParseInt(name, parts[3]));

        if (rect.W <= 0 || rect.H <= 0)
        {
            throw new UsageException($"Option {name} needs positive width and height (got '{text}').", Help);
        }

        return rect;
    }

    public PointI ParsePoint(string name, string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new UsageException($"Option {name} expects x,y (got '{text}').", Help);
        }

        return new PointI(ParseInt(name, parts[0]), ParseInt(name, parts[1]));
    }

    /// <summary>Splits <c>120,40@label</c> into a point and an optional label.</summary>
    public (PointI Point, string? Label) ParseMark(string text)
    {
        var at = text.LastIndexOf('@');
        if (at < 0)
        {
            return (ParsePoint("--mark", text), null);
        }

        return (ParsePoint("--mark", text[..at]), text[(at + 1)..]);
    }

    private int ParseInt(string name, string text)
    {
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw new UsageException($"Option {name} expects integers, got '{text}'.", Help);
        }

        return value;
    }
}
