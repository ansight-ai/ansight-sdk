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
            Config = PairingTestDocumentFactory.CreateSignedConfig(signingKey)
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
    }

    [Fact]
    public void CreateCachedDocument_WhenHostReportsWifi_UpdatesDiscoveryMetadata()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "10.0.0.4",
                hostName: "old-host",
                wifiName: "Old Wi-Fi")
        };
        var connectResponse = new ConnectResponse
        {
            Type = "CONNECT_RESP",
            Ver = 1,
            Accepted = true,
            Reason = "accepted",
            HostId = "host-1",
            HostName = "new-host",
            HostWifiName = "Studio Wi-Fi",
            Message = "accepted",
            WebSocketPort = 41001,
            WebSocketPath = "/ws",
            WebSocketToken = "token"
        };

        var cachedDocument = PairingSessionClient.CreateCachedDocument(
            document,
            IPAddress.Parse("10.0.0.9"),
            connectResponse,
            discoveryPort: 45200);

        Assert.NotNull(cachedDocument.DiscoveryHint);
        Assert.Equal(new[] { "10.0.0.9" }, cachedDocument.DiscoveryHint!.HostAddresses);
        Assert.Equal("new-host", cachedDocument.DiscoveryHint.HostName);
        Assert.Equal("Studio Wi-Fi", cachedDocument.DiscoveryHint.WifiName);
    }
}
