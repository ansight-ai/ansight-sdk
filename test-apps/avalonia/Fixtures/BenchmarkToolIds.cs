#if ANSIGHT_BENCHMARK_TOOLS
namespace Ansight.Avalonia.Benchmarks;

internal static class BenchmarkToolIds
{
    internal const string Prepare = "benchmark.prepare";
    internal const string Snapshot = "benchmark.snapshot";
    internal const string Network = "benchmark.request_network";
    internal const string Pressure = "benchmark.set_pressure";
    internal const string Telemetry = "benchmark.emit_telemetry";
    internal const string Navigation = "benchmark.mark_navigation";
}
#endif
