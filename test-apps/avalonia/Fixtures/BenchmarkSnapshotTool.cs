#if ANSIGHT_BENCHMARK_TOOLS
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkSnapshotTool : BenchmarkTool
{
    private readonly BenchmarkFixtureState state;

    internal BenchmarkSnapshotTool(BenchmarkFixtureState state)
    {
        this.state = state;
    }

    public override string Id => BenchmarkToolIds.Snapshot;
    public override string Name => "Inspect Benchmark Fixtures";
    public override string Description => "Reads fixture state and computes the binary file checksum without preparing or changing fixtures.";
    public override ToolPolicy Policy => ToolPolicy.Read;
    public override ToolSchema ArgumentsSchema => BenchmarkToolSchemas.Empty;
    public override ToolSchema ResultSchema => BenchmarkToolSchemas.Snapshot;

    protected override Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments)
    {
        return Task.FromResult(ToolResult.Success(state.Snapshot()));
    }
}
#endif
