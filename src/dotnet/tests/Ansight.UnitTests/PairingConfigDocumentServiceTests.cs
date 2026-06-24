using System.Security.Cryptography;
using System.Text;
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
    public void TryParseAndValidateConfig_AcceptsLegacyStudioTrustSignature()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, appId: "com.ansight.test");
        var signable = PairingCanonicalJson.SerializePairingConfigWithLegacyTrustForSignature(config);
        var signature = signingKey.SignData(Encoding.UTF8.GetBytes(signable), HashAlgorithmName.SHA256);
        config.Signature = Convert.ToBase64String(signature);
        var json = PairingConfigJson.Serialize(config)
            .Replace(
                ",\"signature\"",
                ",\"trust\":{\"mode\":\"pinned-key+token+challenge\",\"requireTokenOnFirstPair\":true,\"allowLanDiscovery\":false},\"signature\"",
                StringComparison.Ordinal);

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseAndValidateConfig(json, "com.ansight.test", out var parsedConfig, out var error);

        Assert.True(success, error);
        Assert.NotNull(parsedConfig);
        Assert.Equal(config.ConfigId, parsedConfig!.ConfigId);
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
    public void TryParseDocument_WhenConfigPayloadIsProvided_ReturnsConfig()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, configId: "cfg-direct");
        var configJson = PairingConfigJson.Serialize(config);

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseDocument(configJson, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
        Assert.Equal("cfg-direct", document!.Config.ConfigId);
        Assert.Null(document.DiscoveryHint);
    }

    [Fact]
    public void TryParseConfigDocument_WhenConfigPayloadIsProvided_WrapsConfig()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, configId: "cfg-direct-document");
        var configJson = PairingConfigJson.Serialize(config);

        var sut = new PairingConfigDocumentService();

        var success = sut.TryParseConfigDocument(configJson, out var document, out var error);

        Assert.True(success, error);
        Assert.NotNull(document);
        Assert.Equal(Ansight.Pairing.Models.PairingConfigDocument.SchemaName, document!.Schema);
        Assert.Equal("cfg-direct-document", document.Config.ConfigId);
        Assert.Null(document.Discovery);
    }
}
