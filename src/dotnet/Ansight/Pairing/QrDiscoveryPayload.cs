using System.Text.Json;

namespace Ansight.Pairing;

public static class QrDiscoveryPayload
{
    public const string Schema = PairingBootstrapDocument.SchemaName;

    public static PairingBootstrapDocument Create(PairingConfig config, PairingDiscoveryHint? discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new PairingBootstrapDocument
        {
            Schema = SchemaName,
            PairingConfig = config,
            Discovery = discoveryHint
        };
    }

    public static string Serialize(PairingConfig config, PairingDiscoveryHint? discoveryHint, bool indented = false)
    {
        var payload = Create(config, discoveryHint);
        return JsonSerializer.Serialize(payload, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    public static bool TryParse(string payload, out PairingBootstrapDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<PairingBootstrapDocument>(payload, PairingJson.Compact);
            return document is not null && string.Equals(document.Schema, PairingBootstrapDocument.SchemaName, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private const string SchemaName = PairingBootstrapDocument.SchemaName;
}
