using System.Net;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.UnitTests;

public sealed class PairingSessionClientTests
{
    [Fact]
    public void CreateLocalPairingDocument_AdvertisesReadAndWriteWithoutCriticalAccess()
    {
        var appId = $"com.ansight.unit-tests.{Guid.NewGuid():N}";

        var document = LocalPairingDocumentFactory.Create(
            appId,
            "Unit Test App",
            "127.0.0.1",
            45200);

        var enrollment = Assert.IsType<PairingEnrollment>(document.Config.Enrollment);
        Assert.Equal("write", enrollment.MaxToolPolicy);
    }

    [Fact]
    public void CreateSessionOpenPayload_WhenCustomPropertiesAreRegistered_IncludesGroupedProperties()
    {
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite();
        var customProperties = new SessionCustomProperties()
            .Register("app", "tenant", "acme")
            .Register("app", "region", "au")
            .Register("flags", "beta", true);

        var payload = PairingSessionClient.CreateSessionOpenPayload(config, "Unit Test App", customProperties);

        Assert.Equal("Unit Test App", payload["clientName"]?.GetValue<string>());
        Assert.Equal(config.ConfigId, payload["configId"]?.GetValue<string>());
        Assert.Equal(config.AppId, payload["appId"]?.GetValue<string>());

        var properties = Assert.IsType<System.Text.Json.Nodes.JsonObject>(payload["customProperties"]);
        Assert.Equal("acme", properties["app"]?["tenant"]?.GetValue<string>());
        Assert.Equal("au", properties["app"]?["region"]?.GetValue<string>());
        Assert.True(properties["flags"]?["beta"]?.GetValue<bool>());
    }

    [Fact]
    public void CreateSessionPropertiesPayload_WhenCustomPropertiesAreRegistered_IncludesGroupedProperties()
    {
        var customProperties = new SessionCustomProperties()
            .Register("app", "tenant", "acme")
            .Register("flags", "beta", true);

        var payload = PairingSessionClient.CreateSessionPropertiesPayload(customProperties);

        var properties = Assert.IsType<System.Text.Json.Nodes.JsonObject>(payload["customProperties"]);
        Assert.Equal("acme", properties["app"]?["tenant"]?.GetValue<string>());
        Assert.True(properties["flags"]?["beta"]?.GetValue<bool>());
        Assert.NotNull(payload["updatedAtUtc"]);
    }

    [Fact]
    public void CreateSessionPropertiesPayload_WhenCustomPropertiesAreEmpty_IncludesEmptyObject()
    {
        var payload = PairingSessionClient.CreateSessionPropertiesPayload(customProperties: null);

        var properties = Assert.IsType<System.Text.Json.Nodes.JsonObject>(payload["customProperties"]);
        Assert.Empty(properties);
    }

    [Fact]
    public void CreateCachedDocument_WhenConnectedHostAddressIsAvailable_AddsDiscoveryHint()
    {
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite()
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
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "10.0.0.4",
                hostName: "old-host",
                wifiName: "Old Wi-Fi")
        };
        var connectResponse = new ConnectResponse
        {
            Type = "ENROLLMENT_RESULT",
            Ver = 2,
            RequestId = "test-request",
            Accepted = true,
            Reason = "accepted",
            HostId = "host-1",
            HostName = "new-host",
            HostWifiName = "host Wi-Fi",
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
        Assert.Equal("host Wi-Fi", cachedDocument.DiscoveryHint.WifiName);
    }
}
