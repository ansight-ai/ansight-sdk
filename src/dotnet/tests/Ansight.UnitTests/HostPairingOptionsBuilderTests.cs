namespace Ansight.UnitTests;

public sealed class HostPairingOptionsBuilderTests
{
    [Fact]
    public async Task WithBundledHostPairing_WhenUsingBundledAssetLoader_ConfiguresStandardAssetLoaders()
    {
        var requestedAssetNames = new List<string>();
        var payloadReader = new FakeHostPairingPayloadReader();
        var options = Options.CreateBuilder()
            .WithBundledHostPairing(
                (assetName, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    requestedAssetNames.Add(assetName);
                    return Task.FromResult<string?>(assetName);
                },
                payloadReader)
            .Build();

        Assert.NotNull(options.HostPairing.BundledDeveloperProfileLoader);
        Assert.NotNull(options.HostPairing.BundledProfileLoader);
        Assert.Same(payloadReader, options.HostPairing.PayloadReader);

        var bundledDeveloperText = await options.HostPairing.BundledDeveloperProfileLoader!(CancellationToken.None);
        var bundledProfileText = await options.HostPairing.BundledProfileLoader!(CancellationToken.None);

        Assert.Equal(
            [HostPairingOptions.BundledDeveloperAssetName, HostPairingOptions.BundledAssetName],
            requestedAssetNames);
        Assert.Equal(HostPairingOptions.BundledDeveloperAssetName, bundledDeveloperText);
        Assert.Equal(HostPairingOptions.BundledAssetName, bundledProfileText);
    }

    [Fact]
    public void WithBundledHostPairing_WhenUsingAssembly_ConfiguresBundledProfileAssemblyAndPayloadReader()
    {
        var payloadReader = new FakeHostPairingPayloadReader();
        var bundledProfileAssembly = typeof(HostPairingOptionsBuilderTests).Assembly;
        var options = Options.CreateBuilder()
            .WithBundledHostPairing(bundledProfileAssembly, payloadReader)
            .Build();

        Assert.Same(bundledProfileAssembly, options.HostPairing.BundledProfileAssembly);
        Assert.Same(payloadReader, options.HostPairing.PayloadReader);
    }

    [Fact]
    public void WithHostPairingDiscoveryPort_ConfiguresTheBootstrapPortOverride()
    {
        var options = Options.CreateBuilder()
            .WithHostPairingDiscoveryPort(45200)
            .Build();

        Assert.Equal(45200, options.HostPairing.DiscoveryPort);
    }

    private sealed class FakeHostPairingPayloadReader : IHostPairingPayloadReader
    {
        public bool CanRead(HostPairingPayloadReadKind kind)
        {
            return kind == HostPairingPayloadReadKind.File;
        }

        public Task<string?> ReadPayloadAsync(
            HostPairingPayloadReadRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }
}
