#if ANSIGHT_BENCHMARK_TOOLS
using System.Reflection;
using Ansight.Telemetry.Channels;
using Ansight.Tools;
using Ansight.Tools.Reflection;

namespace Ansight.Avalonia.Benchmarks;

internal static class BenchmarkOptionsBuilderExtensions
{
    internal static BenchmarkFixtureState? Current { get; private set; }

    internal static Options.OptionsBuilder WithBenchmarkFixtures(this Options.OptionsBuilder options, Assembly applicationAssembly)
    {
        var state = new BenchmarkFixtureState(applicationAssembly);
        Current = state;
        ReflectionRootRegistry.Register("avalonia.benchmark", state.Model,
            new ReflectionRootMetadata("Avalonia Benchmark Fixture"), ReferenceType.Strong);
        options.AddAdditionalChannel(new Channel(96, "Benchmark fixture", new MetricGroup("benchmark", "Benchmark"),
            System.Drawing.Color.Coral, unit: "count", source: "avalonia.benchmark"));
        options.AddArtifactProvider(new BenchmarkArtifactProvider(state));
        return options.AddTools(new ITool[]
        {
            new BenchmarkPrepareTool(state),
            new BenchmarkSnapshotTool(state),
            new BenchmarkNetworkTool(state),
            new BenchmarkPressureTool(state),
            new BenchmarkTelemetryTool(),
            new BenchmarkNavigationTool()
        });
    }
}
#endif
