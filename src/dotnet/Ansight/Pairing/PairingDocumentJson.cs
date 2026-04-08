using System.Text.Json;
using System.Text.Json.Serialization;
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
            return PairingConfigJson.Serialize(document.Config, indented);
        }

        var bootstrap = new PairingBootstrapDocumentJsonModel
        {
            Schema = PairingBootstrapDocument.SchemaName,
            PairingConfig = PairingConfigJson.CreateJsonModel(document.TrustAnchorConfig ?? document.Config),
            Discovery = document.DiscoveryHint,
            ConnectionHint = document.ConnectionHint
        };

        return JsonSerializer.Serialize(bootstrap, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    private sealed class PairingBootstrapDocumentJsonModel
    {
        public required string Schema { get; init; }

        public required object PairingConfig { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PairingDiscoveryHint? Discovery { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PairingConnectionHint? ConnectionHint { get; init; }
    }
}
