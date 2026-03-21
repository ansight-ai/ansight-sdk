using System.Text.Json;
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

    [Fact]
    public void PairingCodeGenerator_ParsesMinifiedPrefixAlias()
    {
        var compactCode = PairingCodeGenerator.Serialize(PairingTestDocumentFactory.CreateQrConnectionPayload());
        var minifiedCode = $"apm1{compactCode[4..]}";

        var success = PairingCodeGenerator.TryParse(minifiedCode, out var parsedPayload);

        Assert.True(success);
        Assert.NotNull(parsedPayload);
        Assert.Equal("cfg-override", parsedPayload!.Connection.ConfigId);
        Assert.Equal("127.0.0.1", parsedPayload.Discovery!.HostAddress);
    }

    [Fact]
    public void QrDiscoveryPayload_ParsesMinifiedConnectionPayload()
    {
        const string payload = """
                               {"s":"aqpc1","ci":"cfg-mini","ia":1763456789,"ea":"1763460389","ot":"token-mini","ch":{"a":"ECDH-P256","pk":"challenge-mini","rp":1},"ha":"10.0.0.42","hn":"Studio Mini","wn":"Office Wifi","ca":1763457100,"src":"studio-mini"}
                               """;

        var connectionSuccess = QrDiscoveryPayload.TryParseConnectionPayload(payload, out var connectionPayload);
        var discoverySuccess = QrDiscoveryPayload.TryParse(payload, out var discoveryHint);

        Assert.True(connectionSuccess);
        Assert.NotNull(connectionPayload);
        Assert.Equal("cfg-mini", connectionPayload!.Connection.ConfigId);
        Assert.Equal("token-mini", connectionPayload.Connection.OneTimeToken);
        Assert.Equal("challenge-mini", connectionPayload.Connection.Challenge.ChallengePubKey);
        Assert.True(connectionPayload.Connection.Challenge.RequireProofOnFirstPair);
        Assert.Equal("studio-mini", connectionPayload.Connection.Source);
        Assert.NotNull(connectionPayload.Discovery);
        Assert.Equal("10.0.0.42", connectionPayload.Discovery!.HostAddress);
        Assert.Equal("Studio Mini", connectionPayload.Discovery.HostName);
        Assert.Equal("Office Wifi", connectionPayload.Discovery.WifiName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_763_457_100), connectionPayload.Discovery.CapturedAt);
        Assert.True(discoverySuccess);
        Assert.NotNull(discoveryHint);
        Assert.Equal("10.0.0.42", discoveryHint!.HostAddress);
    }

    [Fact]
    public void QrDiscoveryPayload_ParsesMinifiedDiscoveryPayload()
    {
        const string payload = """
                               {"s":"adh1","ha":"172.16.0.15","hn":"Discovery Mini","wn":"Cafe Wifi","ca":"1763457200","src":"studio-mini"}
                               """;

        var success = QrDiscoveryPayload.TryParse(payload, out var discoveryHint);

        Assert.True(success);
        Assert.NotNull(discoveryHint);
        Assert.Equal("172.16.0.15", discoveryHint!.HostAddress);
        Assert.Equal("Discovery Mini", discoveryHint.HostName);
        Assert.Equal("Cafe Wifi", discoveryHint.WifiName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_763_457_200), discoveryHint.CapturedAt);
        Assert.Equal("studio-mini", discoveryHint.Source);
    }

    [Fact]
    public void QrDiscoveryPayload_DoesNotTreatFullPairingConfigAsQrConnectionPayload()
    {
        using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey);
        var configJson = JsonSerializer.Serialize(config, PairingJson.Compact);

        var success = QrDiscoveryPayload.TryParseConnectionPayload(configJson, out _);

        Assert.False(success);
    }
}
