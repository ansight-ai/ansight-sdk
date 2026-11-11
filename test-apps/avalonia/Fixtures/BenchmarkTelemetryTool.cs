#if ANSIGHT_BENCHMARK_TOOLS
using System.Text.Json.Nodes;
using Ansight.Tools;
using Ansight.Telemetry.Events;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkTelemetryTool : BenchmarkTool
{
    public override string Id => BenchmarkToolIds.Telemetry;
    public override string Name => "Emit Benchmark Telemetry";
    public override string Description => "Records a known event, numeric metric, completed span and client log for retained-evidence validation.";
    public override ToolPolicy Policy => ToolPolicy.Write;
    public override ToolSchema ArgumentsSchema => BenchmarkToolSchemas.Empty;
    public override ToolSchema ResultSchema => BenchmarkToolSchemas.MarkerResult;

    protected override async Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments)
    {
        const string label = "avalonia.benchmark.fixture";
        using var span = Runtime.BeginSpan(label, 96);
        Runtime.Event(label, AppEventType.Info, 96, "synthetic-fixture");
        Runtime.Metric(42.25, 96);
        await Runtime.SendClientLogAsync("avalonia.benchmark.fixture.log");
        await Task.Delay(25);
        span.End();
        return ToolResult.Success(new JsonObject { ["label"] = label, ["spanId"] = span.Id });
    }
}
#endif
