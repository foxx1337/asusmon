using System.Text.Json;
using System.Text.Json.Serialization;
using AsusMon.Monitors;

namespace AsusMon.Cli;

/// <summary>
/// Source-generated JSON contracts for <c>--json</c> output.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(JsonStringEnumConverter<ProductLine>), typeof(JsonStringEnumConverter<GameVisualFamily>)])]
[JsonSerializable(typeof(List<DisplaySummary>))]
[JsonSerializable(typeof(DisplaySummary))]
internal sealed partial class AsusMonJsonContext : JsonSerializerContext;
