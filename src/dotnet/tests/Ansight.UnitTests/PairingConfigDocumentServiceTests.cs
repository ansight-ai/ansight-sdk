using System.Security.Cryptography;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingConfigDocumentServiceTests
{
    [Fact]
    public void TryParseDocument_ParsesPairingConfig()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            configId: "cfg-config",
            oneTimeToken: "token-config",
            challengePubKey: "challenge-config");
        var json = PairingTestDocumentFactory.CreateConfigDocumentJson(
            config,
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: "127.0.0.1",
                source: "unit-test"));

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
        Assert.Equal("cfg-config", document!.Config.ConfigId);
        Assert.Equal("token-config", document.Config.OneTimeToken);
        Assert.Equal("challenge-config", document.Config.Challenge.ChallengePubKey);
        Assert.Equal(config.Host.HostPubKey, document.Config.Host.HostPubKey);
        Assert.Equal(new[] { "127.0.0.1" }, document.DiscoveryHint!.HostAddresses);
    }

    [Fact]
    public void TryParseDocument_AcceptsLegacyConfigDocumentSchema()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configDocument = PairingTestDocumentFactory.CreateConfigDocument(
            PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            PairingTestDocumentFactory.CreateDiscoveryHint());
        var json = PairingConfigDocumentJson.Serialize(configDocument)
            .Replace(
                Ansight.Pairing.Models.PairingConfigDocument.SchemaName,
                Ansight.Pairing.Models.PairingConfigDocument.LegacySchemaName,
                StringComparison.Ordinal);

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(json, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
    }

    [Fact]
    public void TryParseDocument_WhenDeveloperPairingMarkerIsProvided_ReturnsFailure()
    {
        var json = """
                   {
                     "schema": "ansight.developer-pairing.v1",
                     "discovery": {
                       "schema": "ansight.discovery-hint.v1",
                       "source": "developer-pairing-msbuild",
                       "hostAddresses": [ "127.0.0.1" ]
                     }
                   }
                   """;

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(json, out var document, out var error);

        Assert.False(success);
        Assert.Null(document);
        Assert.Contains("Unsupported pairing payload schema", error, StringComparison.Ordinal);
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
    public void TryParseAndValidateDocument_WhenConfigIsExpired_ReturnsFalseAndNullDocument()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var expiredConfig = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            appId: "com.ansight.test",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var expiredJson = PairingTestDocumentFactory.CreateConfigDocumentJson(expiredConfig);

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
        Assert.Contains("pairing config", error, StringComparison.OrdinalIgnoreCase);
    }
}
