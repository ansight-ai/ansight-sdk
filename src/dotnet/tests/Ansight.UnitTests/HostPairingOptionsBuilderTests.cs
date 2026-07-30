namespace Ansight.UnitTests;

public sealed class HostConnectionOptionsBuilderTests
{
    [Fact]
    public async Task WithBundledHostConnection_WhenUsingBundledAssetLoader_ConfiguresStandardAssetLoaders()
    {
        var requestedAssetNames = new List<string>();
        var payloadReader = new FakeHostPairingConfigReader();
        var options = Options.CreateBuilder()
            .WithBundledHostConnection(
                (assetName, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    requestedAssetNames.Add(assetName);
                    return Task.FromResult<string?>(assetName);
                },
                payloadReader)
            .Build();

        Assert.NotNull(options.HostConnection.BundledConfigLoader);
        Assert.Same(payloadReader, options.HostConnection.ConfigReader);

        var bundledProfileText = await options.HostConnection.BundledConfigLoader!(CancellationToken.None);

        Assert.Equal([HostConnectionOptions.BundledConfigAssetName], requestedAssetNames);
        Assert.Equal(HostConnectionOptions.BundledConfigAssetName, bundledProfileText);
    }

    [Fact]
    public void WithBundledHostConnection_WhenUsingAssembly_ConfiguresBundledConfigAssemblyAndConfigReader()
    {
        var payloadReader = new FakeHostPairingConfigReader();
        var bundledProfileAssembly = typeof(HostConnectionOptionsBuilderTests).Assembly;
        var options = Options.CreateBuilder()
            .WithBundledHostConnection(bundledProfileAssembly, payloadReader)
            .Build();

        Assert.Same(bundledProfileAssembly, options.HostConnection.BundledConfigAssembly);
        Assert.Same(payloadReader, options.HostConnection.ConfigReader);
    }

    [Fact]
    public void WithHostConnectionDiscoveryPort_ConfiguresTheBootstrapPortOverride()
    {
        var options = Options.CreateBuilder()
            .WithHostConnectionDiscoveryPort(45200)
            .Build();

        Assert.Equal(45200, options.HostConnection.DiscoveryPort);
    }

    [Fact]
    public void WithCellularHostConnections_OptsInWhileTheDefaultRemainsDisabled()
    {
        var defaultOptions = Options.CreateBuilder().Build();
        var cellularOptions = Options.CreateBuilder()
            .WithCellularHostConnections()
            .Build();

        Assert.False(defaultOptions.HostConnection.AllowCellularConnections);
        Assert.True(cellularOptions.HostConnection.AllowCellularConnections);
    }

    [Fact]
    public void WithHostConnectionProfileRetention_ConfiguresCachedProfileRetention()
    {
        var retention = TimeSpan.FromDays(30);
        var options = Options.CreateBuilder()
            .WithHostConnectionProfileRetention(retention)
            .Build();

        Assert.Equal(retention, options.HostConnection.ConnectionProfileRetention);
    }

    private sealed class FakeHostPairingConfigReader : IHostConnectionConfigReader
    {
        public bool CanRead(HostConnectionRequestKind kind)
        {
            return kind == HostConnectionRequestKind.File;
        }

        public Task<string?> ReadConfigPayloadAsync(
            HostConnectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }
}
