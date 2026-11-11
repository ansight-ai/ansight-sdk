#if ANSIGHT_BENCHMARK_TOOLS
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkPressureTool : BenchmarkTool
{
    private readonly BenchmarkFixtureState state;

    internal BenchmarkPressureTool(BenchmarkFixtureState state)
    {
        this.state = state;
    }

    public override string Id => BenchmarkToolIds.Pressure;
    public override string Name => "Set Benchmark Resource Pressure";
    public override string Description => "Holds or releases 32 fixture file handles and, on Android, 32 managed Java references. Repeated enable calls replace the fixture resources.";
    public override ToolPolicy Policy => ToolPolicy.Write;
    public override ToolSchema ArgumentsSchema => ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["enabled"] = ToolSchema.Boolean()
    }, required: ["enabled"]);
    public override ToolSchema ResultSchema => BenchmarkToolSchemas.Snapshot;

    protected override Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("enabled", out var raw) || !bool.TryParse(raw, out var enabled))
        {
            throw new ArgumentException("enabled must be true or false.");
        }

        return Task.FromResult(ToolResult.Success(state.SetPressure(enabled)));
    }
}
#endif
