#if ANSIGHT_BENCHMARK_TOOLS
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkNetworkTool : BenchmarkTool
{
    private readonly BenchmarkFixtureState state;

    internal BenchmarkNetworkTool(BenchmarkFixtureState state)
    {
        this.state = state;
    }

    public override string Id => BenchmarkToolIds.Network;
    public override string Name => "Request Benchmark Network Fixture";
    public override string Description => "Performs one captured HTTP request to the local benchmark server. Supports JSON, HTTP 500, binary response and POST echo scenarios.";
    public override ToolPolicy Policy => ToolPolicy.Write;
    public override ToolSchema ArgumentsSchema => BenchmarkToolSchemas.NetworkArguments;
    public override ToolSchema ResultSchema => BenchmarkToolSchemas.NetworkResult;

    protected override async Task<ToolResult> Run(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("scenario", out var scenario))
        {
            throw new ArgumentException("scenario is required.");
        }

        if (scenario is not ("json" or "error" or "binary" or "echo"))
        {
            throw new ArgumentException("scenario must be json, error, binary or echo.");
        }

        return ToolResult.Success(await state.RequestNetwork(scenario));
    }
}
#endif
