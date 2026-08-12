using System.Text.Json.Serialization;

namespace ComputerUse.Cli.Json;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(CliResponse))]
[JsonSerializable(typeof(Dictionary<string, SnapshotElementDto>))]
[JsonSerializable(typeof(SnapshotElementDto))]
[JsonSerializable(typeof(WindowSnapshotDto))]
internal partial class CliJsonContext : JsonSerializerContext;
