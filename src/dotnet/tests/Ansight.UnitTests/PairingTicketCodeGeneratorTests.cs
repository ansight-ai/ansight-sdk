using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingTicketCodeGeneratorTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsCompactPairingTicket()
    {
        using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var capturedAt = DateTimeOffset.FromUnixTimeSeconds(1_763_457_100);
        var ticket = PairingTestDocumentFactory.CreateTicket(
            PairingTestDocumentFactory.CreateSignedConfig(
                signingKey,
                configId: "cfg-compact",
                oneTimeToken: "token-compact",
                challengePubKey: "challenge-compact"),
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "192.168.1.24",
                hostAddresses: new[] { "192.168.1.24", "fd00::24" },
                discoveryPort: 45200,
                hostName: "Studio Host",
                wifiName: "Office Wifi",
                source: "studio-qr",
                capturedAt: capturedAt));

        var compactCode = PairingTicketCodeGenerator.Serialize(ticket);

        var success = PairingTicketCodeGenerator.TryParse(compactCode, out var parsedTicket);

        Assert.True(success);
        Assert.NotNull(parsedTicket);
        Assert.Equal("cfg-compact", parsedTicket!.Config.ConfigId);
        Assert.Equal("token-compact", parsedTicket.Config.OneTimeToken);
        Assert.Equal("challenge-compact", parsedTicket.Config.Challenge.ChallengePubKey);
        Assert.NotNull(parsedTicket.Discovery);
        Assert.Equal(new[] { "192.168.1.24", "fd00::24" }, parsedTicket.Discovery!.HostAddresses);
        Assert.Equal(45200, parsedTicket.Discovery.DiscoveryPort);
        Assert.Equal("Studio Host", parsedTicket.Discovery.HostName);
        Assert.Equal("Office Wifi", parsedTicket.Discovery.WifiName);
        Assert.Equal(capturedAt, parsedTicket.Discovery.CapturedAt);
    }

    [Fact]
    public void SerializeAndParse_PreservesEscapedTextFields()
    {
        using var signingKey = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var ticket = PairingTestDocumentFactory.CreateTicket(
            PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "10.0.0.25",
                hostName: "Studio\\nHost",
                wifiName: "Office\\Wifi",
                source: "studio\rqr",
                capturedAt: DateTimeOffset.FromUnixTimeSeconds(1_763_457_100)));

        var compactCode = PairingTicketCodeGenerator.Serialize(ticket);

        var success = PairingTicketCodeGenerator.TryParse(compactCode, out var parsedTicket);

        Assert.True(success);
        Assert.NotNull(parsedTicket);
        Assert.Equal("Studio\\nHost", parsedTicket!.Discovery!.HostName);
        Assert.Equal("Office\\Wifi", parsedTicket.Discovery!.WifiName);
        Assert.Equal("studio\rqr", parsedTicket.Discovery.Source);
    }

    [Fact]
    public void TryParse_ReturnsFalseForUnsupportedPayload()
    {
        var success = PairingTicketCodeGenerator.TryParse("apc1:not-a-ticket", out _);

        Assert.False(success);
    }
}
