using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingCodeGeneratorTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsCompactPairingCode()
    {
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(1_763_456_789);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(1_763_460_389);
        var capturedAt = DateTimeOffset.FromUnixTimeSeconds(1_763_457_100);
        var payload = PairingTestDocumentFactory.CreateQrConnectionPayload(
            PairingTestDocumentFactory.CreateConnectionHint(
                configId: "cfg-compact",
                oneTimeToken: "token-compact",
                challengePubKey: "challenge-compact",
                issuedAt: issuedAt,
                expiresAt: expiresAt),
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "192.168.1.24",
                hostName: "Studio Host",
                wifiName: "Office Wifi",
                source: "studio-qr",
                capturedAt: capturedAt));

        var compactCode = PairingCodeGenerator.Serialize(payload);

        var success = PairingCodeGenerator.TryParse(compactCode, out var parsedPayload);

        Assert.True(success);
        Assert.NotNull(parsedPayload);
        Assert.Equal("cfg-compact", parsedPayload!.Connection.ConfigId);
        Assert.Equal("token-compact", parsedPayload.Connection.OneTimeToken);
        Assert.Equal("challenge-compact", parsedPayload.Connection.Challenge.ChallengePubKey);
        Assert.Equal("studio-qr", parsedPayload.Connection.Source);
        Assert.Equal("192.168.1.24", parsedPayload.Discovery!.HostAddress);
        Assert.Equal("Studio Host", parsedPayload.Discovery.HostName);
        Assert.Equal("Office Wifi", parsedPayload.Discovery.WifiName);
        Assert.Equal(capturedAt, parsedPayload.Discovery.CapturedAt);
    }

    [Fact]
    public void SerializeAndParse_PreservesEscapedTextFields()
    {
        var payload = PairingTestDocumentFactory.CreateQrConnectionPayload(
            discoveryHint: PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "10.0.0.25",
                hostName: "Studio\\nHost",
                wifiName: "Office\\Wifi",
                source: "studio\rqr",
                capturedAt: DateTimeOffset.FromUnixTimeSeconds(1_763_457_100)));

        var compactCode = PairingCodeGenerator.Serialize(payload);

        var success = PairingCodeGenerator.TryParse(compactCode, out var parsedPayload);

        Assert.True(success);
        Assert.NotNull(parsedPayload);
        Assert.Equal("Studio\\nHost", parsedPayload!.Discovery!.HostName);
        Assert.Equal("Office\\Wifi", parsedPayload.Discovery.WifiName);
        Assert.Equal("studio\rqr", parsedPayload.Discovery.Source);
        Assert.Equal("studio\rqr", parsedPayload.Connection.Source);
    }

    [Fact]
    public void QrDiscoveryPayload_ParsesCompactPairingCode()
    {
        using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey);
        var discoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
            hostAddress: "172.16.0.4",
            hostName: "CLI Host",
            source: "daemon-cli");
        var compactCode = QrDiscoveryPayload.SerializeCompactCode(config, discoveryHint);

        var connectionSuccess = QrDiscoveryPayload.TryParseConnectionPayload(compactCode, out var connectionPayload);
        var discoverySuccess = QrDiscoveryPayload.TryParse(compactCode, out var parsedDiscoveryHint);

        Assert.True(connectionSuccess);
        Assert.NotNull(connectionPayload);
        Assert.Equal(config.ConfigId, connectionPayload!.Connection.ConfigId);
        Assert.Equal("daemon-cli", connectionPayload.Discovery!.Source);
        Assert.True(discoverySuccess);
        Assert.NotNull(parsedDiscoveryHint);
        Assert.Equal("172.16.0.4", parsedDiscoveryHint!.HostAddress);
        Assert.Equal("CLI Host", parsedDiscoveryHint.HostName);
    }
}
