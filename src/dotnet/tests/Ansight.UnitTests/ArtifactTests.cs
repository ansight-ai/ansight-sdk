using System.Text;
using System.Text.Json.Nodes;
using Ansight.Artifacts;
using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class ArtifactTests
{
    [Fact]
    public void AddArtifactProvider_RegistersProviderAndCoreTools()
    {
        var provider = new TestArtifactProvider("app.report");

        var options = Options.CreateBuilder()
            .AddArtifactProvider(provider)
            .Build();

        Assert.True(options.ArtifactProviders.Contains("app.report"));
        Assert.Contains(options.Tools, tool => tool.Id == ArtifactToolIds.Query);
        Assert.Contains(options.Tools, tool => tool.Id == ArtifactToolIds.Request);
    }

    [Fact]
    public void ArtifactRegistry_RejectsDuplicateProviderIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ArtifactRegistry(new[]
            {
                new TestArtifactProvider("app.report"),
                new TestArtifactProvider("APP.REPORT")
            }));

        Assert.Contains("already been registered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArtifactPayload_FromText_OpensFreshReadableStreams()
    {
        var payload = ArtifactPayload.FromText("hello", Encoding.UTF8);

        await using var first = await payload.OpenReadAsync();
        await using var second = await payload.OpenReadAsync();

        Assert.Equal(5, payload.SizeBytes);
        Assert.NotSame(first, second);
        Assert.Equal("hello", await new StreamReader(first, Encoding.UTF8).ReadToEndAsync());
        Assert.Equal("hello", await new StreamReader(second, Encoding.UTF8).ReadToEndAsync());
    }

    [Fact]
    public async Task QueryArtifactsTool_Execute_ReturnsProviderAndDefinitions()
    {
        var provider = new TestArtifactProvider("app.report");
        var tool = new QueryArtifactsTool(new ArtifactRegistry(new[] { provider }));

        var result = await tool.Execute(new Dictionary<string, string>
        {
            [ToolExecutionArgumentNames.RequestId] = "req_1",
            [ToolExecutionArgumentNames.SessionId] = "sess_1"
        });

        Assert.True(result.IsSuccess, result.Message);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal(1, payload["providerCount"]?.GetValue<int>());
        Assert.Equal(1, payload["artifactCount"]?.GetValue<int>());

        var providers = Assert.IsType<JsonArray>(payload["providers"]);
        var providerJson = Assert.IsType<JsonObject>(Assert.Single(providers));
        Assert.Equal("app.report", providerJson["id"]?.GetValue<string>());
        Assert.Equal("reports", providerJson["category"]?.GetValue<string>());

        var artifacts = Assert.IsType<JsonArray>(payload["artifacts"]);
        var artifactJson = Assert.IsType<JsonObject>(Assert.Single(artifacts));
        Assert.Equal("app.report", artifactJson["providerId"]?.GetValue<string>());
        Assert.Equal("current", artifactJson["id"]?.GetValue<string>());
        Assert.Equal("report", artifactJson["kind"]?.GetValue<string>());

        var metadata = Assert.IsType<JsonObject>(artifactJson["metadata"]);
        Assert.Equal("current", metadata["scope"]?.GetValue<string>());
    }

    [Fact]
    public async Task RequestArtifactTool_Execute_ReturnsUnavailableWithoutLiveTransferHub()
    {
        var provider = new TestArtifactProvider("app.report");
        var tool = new RequestArtifactTool(
            new ArtifactRegistry(new[] { provider }),
            transferHubFactory: static () => null);

        var result = await tool.Execute(new Dictionary<string, string>
        {
            [ToolExecutionArgumentNames.RequestId] = "req_1",
            ["providerId"] = "app.report",
            ["artifactId"] = "current"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("artifact_transfer_unavailable", result.ErrorCode);
    }

    private sealed class TestArtifactProvider : IArtifactProvider
    {
        public TestArtifactProvider(string id)
        {
            Descriptor = new ArtifactProviderDescriptor(
                id,
                "Report Provider",
                "Provides app reports.",
                "reports")
            {
                Tags = new[] { "reports" },
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["owner"] = "unit-test"
                }
            };
        }

        public ArtifactProviderDescriptor Descriptor { get; }

        public Task<IReadOnlyList<ArtifactDefinition>> QueryAsync(
            ArtifactQueryContext context,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ArtifactDefinition> definitions =
            [
                new ArtifactDefinition(
                    "current",
                    "Current Report",
                    "Exports the current report.",
                    "report",
                    "reports",
                    new ArtifactContentDescriptor(new[] { "text/csv" })
                    {
                        DefaultMimeType = "text/csv",
                        SuggestedFileName = "current-report.csv",
                        SupportsText = true,
                        SizeKnownBeforeCreation = false
                    },
                    ToolSchema.Object(),
                    ToolPolicy.Read)
                {
                    Tags = new[] { "csv" },
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["scope"] = "current"
                    }
                }
            ];

            return Task.FromResult(definitions);
        }

        public Task<ArtifactResult> CreateAsync(
            ArtifactRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = new ArtifactResult(
                new ArtifactMetadata(
                    request.ArtifactId,
                    request.ProviderId,
                    "Current Report",
                    "report",
                    "text/csv",
                    "current-report.csv"),
                ArtifactPayload.FromText("id,name\n1,Ada\n"));

            return Task.FromResult(result);
        }
    }
}
