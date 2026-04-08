namespace Ansight.UnitTests;

public sealed class StudioConnectionOptionsBuilderTests
{
    [Fact]
    public async Task WithBundledStudioConnection_WhenUsingBundledAssetLoader_ConfiguresStandardAssetLoaders()
    {
        var requestedAssetNames = new List<string>();
        var payloadReader = new FakeHostPairingTicketReader();
        var options = Options.CreateBuilder()
            .WithBundledStudioConnection(
                (assetName, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    requestedAssetNames.Add(assetName);
                    return Task.FromResult<string?>(assetName);
                },
                payloadReader)
            .Build();

        Assert.NotNull(options.StudioConnection.BundledDeveloperTicketLoader);
        Assert.NotNull(options.StudioConnection.BundledTicketLoader);
        Assert.Same(payloadReader, options.StudioConnection.TicketReader);

        var bundledDeveloperText = await options.StudioConnection.BundledDeveloperTicketLoader!(CancellationToken.None);
        var bundledProfileText = await options.StudioConnection.BundledTicketLoader!(CancellationToken.None);

        Assert.Equal(
            [StudioConnectionOptions.BundledDeveloperTicketAssetName, StudioConnectionOptions.BundledTicketAssetName],
            requestedAssetNames);
        Assert.Equal(StudioConnectionOptions.BundledDeveloperTicketAssetName, bundledDeveloperText);
        Assert.Equal(StudioConnectionOptions.BundledTicketAssetName, bundledProfileText);
    }

    [Fact]
    public void WithBundledStudioConnection_WhenUsingAssembly_ConfiguresBundledTicketAssemblyAndTicketReader()
    {
        var payloadReader = new FakeHostPairingTicketReader();
        var bundledProfileAssembly = typeof(StudioConnectionOptionsBuilderTests).Assembly;
        var options = Options.CreateBuilder()
            .WithBundledStudioConnection(bundledProfileAssembly, payloadReader)
            .Build();

        Assert.Same(bundledProfileAssembly, options.StudioConnection.BundledTicketAssembly);
        Assert.Same(payloadReader, options.StudioConnection.TicketReader);
    }

    [Fact]
    public void WithStudioConnectionDiscoveryPort_ConfiguresTheBootstrapPortOverride()
    {
        var options = Options.CreateBuilder()
            .WithStudioConnectionDiscoveryPort(45200)
            .Build();

        Assert.Equal(45200, options.StudioConnection.DiscoveryPort);
    }

    private sealed class FakeHostPairingTicketReader : IStudioConnectionTicketReader
    {
        public bool CanRead(StudioConnectionRequestKind kind)
        {
            return kind == StudioConnectionRequestKind.File;
        }

        public Task<string?> ReadTicketPayloadAsync(
            StudioConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }
}
