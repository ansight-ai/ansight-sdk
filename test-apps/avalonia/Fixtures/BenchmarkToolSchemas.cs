#if ANSIGHT_BENCHMARK_TOOLS
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal static class BenchmarkToolSchemas
{
    internal static ToolSchema Empty { get; } = ToolSchema.Object();
    internal static ToolSchema Snapshot { get; } = ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["rootPath"] = ToolSchema.String(),
        ["prepared"] = ToolSchema.Boolean(),
        ["binarySize"] = ToolSchema.Integer(),
        ["binarySha256"] = ToolSchema.String(nullable: true),
        ["openFixtureHandles"] = ToolSchema.Integer(),
        ["heldJniReferences"] = ToolSchema.Integer(),
        ["counter"] = ToolSchema.Integer(),
        ["label"] = ToolSchema.String()
    }, required: ["rootPath", "prepared", "binarySize", "openFixtureHandles", "heldJniReferences", "counter", "label"]);

    internal static ToolSchema NetworkArguments { get; } = ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["scenario"] = ToolSchema.String(enumValues: ["json", "error", "binary", "echo"])
    }, required: ["scenario"]);

    internal static ToolSchema NetworkResult { get; } = ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["requestId"] = ToolSchema.String(),
        ["scenario"] = ToolSchema.String(),
        ["statusCode"] = ToolSchema.Integer(),
        ["bodyBytes"] = ToolSchema.Integer(),
        ["bodySha256"] = ToolSchema.String(),
        ["bodyText"] = ToolSchema.String(nullable: true)
    }, required: ["requestId", "scenario", "statusCode", "bodyBytes", "bodySha256"]);

    internal static ToolSchema MarkerResult { get; } = ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["label"] = ToolSchema.String(),
        ["spanId"] = ToolSchema.String(nullable: true)
    }, required: ["label"]);
}
#endif
