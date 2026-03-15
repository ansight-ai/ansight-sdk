using System.Text.Json;

namespace Ansight.Pairing;

public static class QrDiscoveryPayload
{
    public const string Schema = PairingDiscoveryHint.SchemaName;

    public static PairingDiscoveryHint Create(PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(discoveryHint);

        discoveryHint.Schema = SchemaName;
        return discoveryHint;
    }

    public static string Serialize(PairingDiscoveryHint discoveryHint, bool indented = false)
    {
        var payload = Create(discoveryHint);
        return JsonSerializer.Serialize(payload, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    public static bool TryParse(string payload, out PairingDiscoveryHint? discoveryHint)
    {
        discoveryHint = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            discoveryHint = JsonSerializer.Deserialize<PairingDiscoveryHint>(payload, PairingJson.Compact);
            return discoveryHint is not null &&
                   string.Equals(discoveryHint.Schema, PairingDiscoveryHint.SchemaName, StringComparison.Ordinal) &&
                   !string.IsNullOrWhiteSpace(discoveryHint.HostAddress);
        }
        catch
        {
            return false;
        }
    }

    private const string SchemaName = PairingDiscoveryHint.SchemaName;
}
