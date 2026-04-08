using System.Net;
using System.Security.Cryptography;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingSessionClientTests
{
    [Fact]
    public void CreateCachedDocument_WhenConnectedHostAddressIsAvailable_AddsDiscoveryHint()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            ConnectionHint = PairingTestDocumentFactory.CreateConnectionHint()
        };
        var capturedAt = new DateTimeOffset(2026, 03, 22, 03, 50, 00, TimeSpan.Zero);

        var cachedDocument = PairingSessionClient.CreateCachedDocument(
            document,
            IPAddress.Parse("10.0.0.8"),
            discoveryPort: 45200,
            capturedAt);

        Assert.NotNull(cachedDocument.DiscoveryHint);
        Assert.Equal(PairingDiscoveryHint.SchemaName, cachedDocument.DiscoveryHint!.Schema);
        Assert.Equal("live-session", cachedDocument.DiscoveryHint.Source);
        Assert.Equal(new[] { "10.0.0.8" }, cachedDocument.DiscoveryHint.HostAddresses);
        Assert.Equal(45200, cachedDocument.DiscoveryHint.DiscoveryPort);
        Assert.Equal(capturedAt, cachedDocument.DiscoveryHint.CapturedAt);
        Assert.Same(document.Config, cachedDocument.Config);
        Assert.Same(document.ConnectionHint, cachedDocument.ConnectionHint);
    }

    [Fact]
    public void CreatePreferredDocument_StripsRememberedHostAddressButKeepsMetadata()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var capturedAt = new DateTimeOffset(2026, 03, 22, 03, 50, 00, TimeSpan.Zero);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "10.0.0.8",
                hostAddresses: new[] { "10.0.0.8", "fd00::8" },
                discoveryPort: 45200,
                hostName: "Studio",
                wifiName: "Office Wifi",
                capturedAt: capturedAt),
            ConnectionHint = PairingTestDocumentFactory.CreateConnectionHint()
        };

        var preferredDocument = PairingSessionClient.CreatePreferredDocument(document);

        Assert.NotNull(preferredDocument.DiscoveryHint);
        Assert.NotNull(preferredDocument.DiscoveryHint!);
        Assert.Null(preferredDocument.DiscoveryHint.HostAddresses);
        Assert.Equal(45200, preferredDocument.DiscoveryHint.DiscoveryPort);
        Assert.Equal("Studio", preferredDocument.DiscoveryHint.HostName);
        Assert.Equal("Office Wifi", preferredDocument.DiscoveryHint.WifiName);
        Assert.Equal(capturedAt, preferredDocument.DiscoveryHint.CapturedAt);
        Assert.Same(document.Config, preferredDocument.Config);
        Assert.Same(document.ConnectionHint, preferredDocument.ConnectionHint);
    }
}
