using System.Text.Json;
using System.Text.Json.Serialization;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

public static class PairingConfigDocumentJson
{
    public static string Serialize(PairingConfigDocument document, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var normalizedDiscovery = document.Discovery is null
            ? null
            : PairingDiscoveryHintHostAddresses.NormalizeInPlace(document.Discovery);

        var model = new PairingConfigDocumentJsonModel
        {
            Schema = PairingConfigDocument.SchemaName,
            Config = PairingConfigJson.CreateJsonModel(document.Config),
            Discovery = normalizedDiscovery
        };

        return JsonSerializer.Serialize(model, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    private sealed class PairingConfigDocumentJsonModel
    {
        public required string Schema { get; init; }

        public required object Config { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PairingDiscoveryHint? Discovery { get; init; }
    }
}
