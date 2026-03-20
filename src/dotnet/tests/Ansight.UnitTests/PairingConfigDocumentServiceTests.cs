using System.Security.Cryptography;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingConfigDocumentServiceTests
{
    [Fact]
    public void TryParseDocument_AppliesConnectionHintFromBootstrapDocument()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trustAnchor = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            configId: "cfg-trust",
            oneTimeToken: "token-trust",
            challengePubKey: "challenge-trust");
        var connectionHint = PairingTestDocumentFactory.CreateConnectionHint(
            configId: "cfg-effective",
            oneTimeToken: "token-effective",
            challengePubKey: "challenge-effective");
        var json = PairingTestDocumentFactory.CreateBootstrapJson(trustAnchor, connectionHint);

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
        Assert.Equal("cfg-effective", document!.Config.ConfigId);
        Assert.Equal("token-effective", document.Config.OneTimeToken);
        Assert.Equal("challenge-effective", document.Config.Challenge.ChallengePubKey);
        Assert.Equal(trustAnchor.Host.HostPubKey, document.Config.Host.HostPubKey);
        Assert.NotNull(document.TrustAnchorConfig);
        Assert.Equal("cfg-trust", document.TrustAnchorConfig!.ConfigId);
        Assert.NotNull(document.ConnectionHint);
    }

    [Fact]
    public void TryValidateDocument_ReturnsFalseWhenExpectedAppIdDoesNotMatch()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, appId: "com.ansight.actual");
        var document = new ParsedPairingDocument
        {
            Config = config
        };

        var sut = new PairingConfigDocumentService();

        var success = sut.TryValidateDocument(document, "com.ansight.expected", out var error);

        Assert.False(success);
        Assert.Contains("does not match expected app id", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateConfig_AcceptsValidSignedConfig()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, appId: "com.ansight.test");

        var sut = new PairingConfigDocumentService();

        var success = sut.TryValidateConfig(config, "com.ansight.test", out var error);

        Assert.True(success, error);
        Assert.Equal(string.Empty, error);
    }
}
