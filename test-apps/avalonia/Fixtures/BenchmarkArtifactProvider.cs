#if ANSIGHT_BENCHMARK_TOOLS
using Ansight.Artifacts;
using Ansight.Tools;

namespace Ansight.Avalonia.Benchmarks;

internal sealed class BenchmarkArtifactProvider : IArtifactProvider
{
    private readonly BenchmarkFixtureState state;

    internal BenchmarkArtifactProvider(BenchmarkFixtureState state)
    {
        this.state = state;
    }

    public ArtifactProviderDescriptor Descriptor { get; } = new ArtifactProviderDescriptor(
        "benchmark.fixtures", "Benchmark Fixtures", "Synthetic artifacts for transfer and content validation.", "benchmark");

    public Task<IReadOnlyList<ArtifactDefinition>> QueryAsync(ArtifactQueryContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ArtifactDefinition> definitions =
        [
            new ArtifactDefinition("binary", "Fixture Binary", "A deterministic 256 KiB binary file.", "file", "benchmark",
                new ArtifactContentDescriptor(["application/octet-stream"])
                {
                    DefaultMimeType = "application/octet-stream",
                    SuggestedFileName = "benchmark-binary.bin",
                    SizeKnownBeforeCreation = true
                }, ToolSchema.Object(), ToolPolicy.Read),
            new ArtifactDefinition("report", "Fixture Report", "Current synthetic fixture state as JSON.", "report", "benchmark",
                new ArtifactContentDescriptor(["application/json"])
                {
                    DefaultMimeType = "application/json",
                    SuggestedFileName = "benchmark-report.json",
                    SupportsText = true
                }, ToolSchema.Object(), ToolPolicy.Read)
        ];
        return Task.FromResult(definitions);
    }

    public Task<ArtifactResult> CreateAsync(ArtifactRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ArtifactId == "binary")
        {
            if (!File.Exists(state.BinaryPath))
            {
                throw new InvalidOperationException("Call benchmark.prepare before requesting the binary artifact.");
            }

            return Task.FromResult(new ArtifactResult(
                new ArtifactMetadata("binary", Descriptor.Id, "Fixture Binary", "file", "application/octet-stream", "benchmark-binary.bin"),
                ArtifactPayload.FromFile(state.BinaryPath)));
        }

        if (request.ArtifactId == "report")
        {
            return Task.FromResult(new ArtifactResult(
                new ArtifactMetadata("report", Descriptor.Id, "Fixture Report", "report", "application/json", "benchmark-report.json"),
                ArtifactPayload.FromText(state.Snapshot().ToJsonString())));
        }

        throw new ArgumentException("Unknown benchmark artifact.");
    }
}
#endif
