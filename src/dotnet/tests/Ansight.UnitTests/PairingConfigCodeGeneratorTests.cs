using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingConfigCodeGeneratorTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsCompactEnrollmentInvite()
    {
        var capturedAt = DateTimeOffset.FromUnixTimeSeconds(1_763_457_100);
        var document = PairingTestDocumentFactory.CreateConfigDocument(
            PairingTestDocumentFactory.CreateEnrollmentInvite(configId: "invite-compact"),
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddresses: ["192.168.1.24", "fd00::24"],
                discoveryPort: 45200,
                hostName: "Host Node",
                wifiName: "Office Wifi",
                source: "studio-qr",
                capturedAt: capturedAt));

        var compactCode = PairingConfigCodeGenerator.Serialize(document);
        var success = PairingConfigCodeGenerator.TryParse(compactCode, out var parsed);

        Assert.True(success);
        Assert.StartsWith("ans2:", compactCode, StringComparison.Ordinal);
        Assert.Equal("invite-compact", parsed!.Config.ConfigId);
        Assert.Equal(document.Config.Enrollment!.Secret, parsed.Config.Enrollment!.Secret);
        Assert.Equal(["192.168.1.24", "fd00::24"], parsed.Discovery!.HostAddresses!);
        Assert.Equal(capturedAt, parsed.Discovery.CapturedAt);
    }

    [Fact]
    public void TryParse_ReturnsFalseForMalformedCompactInvite()
    {
        Assert.False(PairingConfigCodeGenerator.TryParse("ans2:not-an-invite", out _));
    }
}
