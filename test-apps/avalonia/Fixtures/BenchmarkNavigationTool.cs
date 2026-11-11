#if ANSIGHT_BENCHMARK_TOOLS
using System.Text.Json.Nodes;
using Ansight.Tools;
using Ansight.Telemetry.Events;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkNavigationTool : BenchmarkTool
{
    public override string Id => BenchmarkToolIds.Navigation;
    public override string Name => "Mark Benchmark Navigation";
    public override string Description => "Records the start or finish anchor around a real sample navigation journey for rendering trends.";
    public override ToolPolicy Policy => ToolPolicy.Write;
    public override ToolSchema ArgumentsSchema => ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["phase"] = ToolSchema.String(enumValues: ["started", "finished"])
    }, required: ["phase"]);
    public override ToolSchema ResultSchema => BenchmarkToolSchemas.MarkerResult;

    protected override Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("phase", out var phase) || phase is not ("started" or "finished"))
        {
            throw new ArgumentException("phase must be started or finished.");
        }

        var label = $"avalonia.benchmark.navigation.{phase}";
        Runtime.Event(label, AppEventType.Info, 96);
        return Task.FromResult(ToolResult.Success(new JsonObject { ["label"] = label }));
    }
}
#endif
