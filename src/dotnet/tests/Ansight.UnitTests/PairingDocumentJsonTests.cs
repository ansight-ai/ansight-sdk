using System.Security.Cryptography;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingDocumentJsonTests
{
    [Fact]
    public void Serialize_AlwaysEmitsTheTrimmedPublicPairingConfigShape()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            hostId: "host-legacy",
            hostName: "Legacy Studio",
            discoveryPort: 41000);
        var document = new ParsedPairingDocument
        {
            Config = config
        };

        var json = PairingDocumentJson.Serialize(document, indented: true);

        Assert.DoesNotContain("\"hostId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"hostName\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"discoveryPort\"", json, StringComparison.Ordinal);

        var documentService = new PairingConfigDocumentService();
        Assert.True(documentService.TryParseAndValidateDocument(json, config.AppId, out var parsedDocument, out var error), error);
        Assert.NotNull(parsedDocument);
        Assert.Null(parsedDocument!.Config.Host.HostId);
        Assert.Null(parsedDocument.Config.Host.HostName);
        Assert.Equal(PairingProtocolDefaults.DiscoveryPort, parsedDocument.Config.Host.DiscoveryPort);
    }
}
