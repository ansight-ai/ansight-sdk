using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingConfigCodeGeneratorTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsCompactPairingConfig()
    {
        using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var capturedAt = DateTimeOffset.FromUnixTimeSeconds(1_763_457_100);
        var configDocument = PairingTestDocumentFactory.CreateConfigDocument(
            PairingTestDocumentFactory.CreateSignedConfig(
                signingKey,
                configId: "cfg-compact",
                oneTimeToken: "token-compact",
                challengePubKey: "challenge-compact"),
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "192.168.1.24",
                hostAddresses: new[] { "192.168.1.24", "fd00::24" },
                discoveryPort: 45200,
                hostName: "Host Node",
                wifiName: "Office Wifi",
                source: "studio-qr",
                capturedAt: capturedAt));

        var compactCode = PairingConfigCodeGenerator.Serialize(configDocument);

        var success = PairingConfigCodeGenerator.TryParse(compactCode, out var parsedConfigDocument);

        Assert.True(success);
        Assert.NotNull(parsedConfigDocument);
        Assert.Equal("cfg-compact", parsedConfigDocument!.Config.ConfigId);
        Assert.Equal("token-compact", parsedConfigDocument.Config.OneTimeToken);
        Assert.Equal("challenge-compact", parsedConfigDocument.Config.Challenge.ChallengePubKey);
        Assert.NotNull(parsedConfigDocument.Discovery);
        Assert.Equal(new[] { "192.168.1.24", "fd00::24" }, parsedConfigDocument.Discovery!.HostAddresses);
        Assert.Equal(45200, parsedConfigDocument.Discovery.DiscoveryPort);
        Assert.Equal("Host Node", parsedConfigDocument.Discovery.HostName);
        Assert.Equal("Office Wifi", parsedConfigDocument.Discovery.WifiName);
        Assert.Equal(capturedAt, parsedConfigDocument.Discovery.CapturedAt);
    }

    [Fact]
    public void SerializeAndParse_PreservesEscapedTextFields()
    {
        using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var configDocument = PairingTestDocumentFactory.CreateConfigDocument(
            PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "10.0.0.25",
                hostName: "Host\\nNode",
                wifiName: "Office\\Wifi",
                source: "studio\rqr",
                capturedAt: DateTimeOffset.FromUnixTimeSeconds(1_763_457_100)));

        var compactCode = PairingConfigCodeGenerator.Serialize(configDocument);

        var success = PairingConfigCodeGenerator.TryParse(compactCode, out var parsedConfigDocument);

        Assert.True(success);
        Assert.NotNull(parsedConfigDocument);
        Assert.Equal("Host\\nNode", parsedConfigDocument!.Discovery!.HostName);
        Assert.Equal("Office\\Wifi", parsedConfigDocument.Discovery!.WifiName);
        Assert.Equal("studio\rqr", parsedConfigDocument.Discovery.Source);
    }

    [Fact]
    public void TryParse_ReturnsFalseForUnsupportedPayload()
    {
        var success = PairingConfigCodeGenerator.TryParse("apc1:not-a-config", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_AcceptsLegacyCompactPrefix()
    {
        var compactCode = PairingTestDocumentFactory.CreateCompactConfigDocument();
        var legacyCompactCode = compactCode.Replace(
            $"{PairingConfigCodeGenerator.FormatPrefix}:",
            $"{PairingConfigCodeGenerator.LegacyFormatPrefix}:",
            StringComparison.Ordinal);

        var success = PairingConfigCodeGenerator.TryParse(legacyCompactCode, out var parsedConfigDocument);

        Assert.True(success);
        Assert.NotNull(parsedConfigDocument);
    }
}
