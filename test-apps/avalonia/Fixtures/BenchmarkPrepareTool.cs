#if ANSIGHT_BENCHMARK_TOOLS
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkPrepareTool : BenchmarkTool
{
    private readonly BenchmarkFixtureState state;

    internal BenchmarkPrepareTool(BenchmarkFixtureState state)
    {
        this.state = state;
    }

    public override string Id => BenchmarkToolIds.Prepare;
    public override string Name => "Prepare Benchmark Fixtures";
    public override string Description => "Writes known text, binary and SQLite fixtures inside the app's benchmark directory; resets only fixture counters and held resources.";
    public override ToolPolicy Policy => ToolPolicy.Write;
    public override ToolSchema ArgumentsSchema => BenchmarkToolSchemas.Empty;
    public override ToolSchema ResultSchema => BenchmarkToolSchemas.Snapshot;

    protected override Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments)
    {
        return Task.FromResult(ToolResult.Success(state.Prepare()));
    }
}
#endif
