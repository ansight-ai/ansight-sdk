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

    public static string Serialize(PairingConfig config, PairingDiscoveryHint discoveryHint, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        var payload = new PairingQrConnectionPayload
        {
            Schema = PairingQrConnectionPayload.SchemaName,
            Connection = CreateConnectionHint(config, discoveryHint.Source),
            Discovery = Create(discoveryHint)
        };

        return JsonSerializer.Serialize(payload, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    public static PairingConnectionHint CreateConnectionHint(PairingConfig config, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new PairingConnectionHint
        {
            Schema = PairingConnectionHint.SchemaName,
            Source = source,
            ConfigId = config.ConfigId,
            IssuedAt = config.IssuedAt,
            ExpiresAt = config.ExpiresAt,
            OneTimeToken = config.OneTimeToken,
            Challenge = new PairingChallenge
            {
                Alg = config.Challenge.Alg,
                ChallengePubKey = config.Challenge.ChallengePubKey,
                RequireProofOnFirstPair = config.Challenge.RequireProofOnFirstPair
            }
        };
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
            if (TryParseConnectionPayload(payload, out var connectionPayload))
            {
                discoveryHint = connectionPayload!.Discovery;
                return discoveryHint is not null &&
                       string.Equals(discoveryHint.Schema, PairingDiscoveryHint.SchemaName, StringComparison.Ordinal) &&
                       !string.IsNullOrWhiteSpace(discoveryHint.HostAddress);
            }

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

    public static bool TryParseConnectionPayload(string payload, out PairingQrConnectionPayload? connectionPayload)
    {
        connectionPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            connectionPayload = JsonSerializer.Deserialize<PairingQrConnectionPayload>(payload, PairingJson.Compact);
            return connectionPayload?.Connection is not null &&
                   string.Equals(connectionPayload.Schema, PairingQrConnectionPayload.SchemaName, StringComparison.Ordinal) &&
                   string.Equals(connectionPayload.Connection.Schema, PairingConnectionHint.SchemaName, StringComparison.Ordinal) &&
                   !string.IsNullOrWhiteSpace(connectionPayload.Connection.ConfigId) &&
                   !string.IsNullOrWhiteSpace(connectionPayload.Connection.OneTimeToken);
        }
        catch
        {
            connectionPayload = null;
            return false;
        }
    }

    private const string SchemaName = PairingDiscoveryHint.SchemaName;
}
