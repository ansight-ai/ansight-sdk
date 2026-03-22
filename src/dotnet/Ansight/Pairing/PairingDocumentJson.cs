using System.Text.Json;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

internal static class PairingDocumentJson
{
    public static string Serialize(ParsedPairingDocument document, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.DiscoveryHint is null &&
            document.ConnectionHint is null &&
            document.TrustAnchorConfig is null)
        {
            return JsonSerializer.Serialize(document.Config, indented ? PairingJson.Pretty : PairingJson.Compact);
        }

        var bootstrap = new PairingBootstrapDocument
        {
            Schema = PairingBootstrapDocument.SchemaName,
            PairingConfig = document.TrustAnchorConfig ?? document.Config,
            Discovery = document.DiscoveryHint,
            ConnectionHint = document.ConnectionHint
        };

        return JsonSerializer.Serialize(bootstrap, indented ? PairingJson.Pretty : PairingJson.Compact);
    }
}
