using System.Security.Cryptography;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingConfigDocumentServiceTests
{
    [Fact]
    public void TryParseDocument_ParsesPairingTicket()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            configId: "cfg-ticket",
            oneTimeToken: "token-ticket",
            challengePubKey: "challenge-ticket");
        var json = PairingTestDocumentFactory.CreateTicketJson(
            config,
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "127.0.0.1",
                source: "unit-test"));

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
        Assert.Equal("cfg-ticket", document!.Config.ConfigId);
        Assert.Equal("token-ticket", document.Config.OneTimeToken);
        Assert.Equal("challenge-ticket", document.Config.Challenge.ChallengePubKey);
        Assert.Equal(config.Host.HostPubKey, document.Config.Host.HostPubKey);
        Assert.Equal(new[] { "127.0.0.1" }, document.DiscoveryHint!.HostAddresses);
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

    [Fact]
    public void TryParseAndValidateDocument_WhenTicketIsExpired_ReturnsFalseAndNullDocument()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var expiredConfig = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            appId: "com.ansight.test",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var expiredJson = PairingTestDocumentFactory.CreateTicketJson(expiredConfig);

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseAndValidateDocument(
            expiredJson,
            "com.ansight.test",
            out var document,
            out var error);

        Assert.False(success);
        Assert.Null(document);
        Assert.Contains("expired", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseDocument_WhenBootstrapPayloadIsProvided_ReturnsFailure()
    {
        const string legacyBootstrapJson = """
                                           {
                                             "schema": "ansight.pairing-bootstrap.v1",
                                             "pairingConfig": {}
                                           }
                                           """;

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(legacyBootstrapJson, out var document, out var error);

        Assert.False(success);
        Assert.Null(document);
        Assert.Contains("no longer supported", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParseDocument_WhenConfigPayloadIsProvided_ReturnsFailure()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configJson = JsonSerializer.Serialize(PairingTestDocumentFactory.CreateSignedConfig(signingKey));

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(configJson, out var document, out var error);

        Assert.False(success);
        Assert.Null(document);
        Assert.Contains("pairing ticket", error, StringComparison.OrdinalIgnoreCase);
    }
}
