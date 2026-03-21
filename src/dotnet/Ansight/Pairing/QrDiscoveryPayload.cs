using System.Text.Json;

namespace Ansight.Pairing;

public static partial class QrDiscoveryPayload
{
    public const string Schema = PairingDiscoveryHint.SchemaName;

    public static PairingDiscoveryHint Create(PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(discoveryHint);

        discoveryHint.Schema = schemaName;
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

        var payload = CreateConnectionPayload(config, discoveryHint);

        return JsonSerializer.Serialize(payload, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    public static string SerializeCompactCode(PairingConfig config, PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(discoveryHint);

        return PairingCodeGenerator.Serialize(CreateConnectionPayload(config, discoveryHint));
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
                return IsValidDiscoveryHint(discoveryHint);
            }

            discoveryHint = JsonSerializer.Deserialize<PairingDiscoveryHint>(payload, PairingJson.Compact);
            if (IsValidDiscoveryHint(discoveryHint))
            {
                return true;
            }

            return TryParseMinifiedDiscoveryPayload(payload, out discoveryHint);
        }
        catch
        {
            return TryParseMinifiedDiscoveryPayload(payload, out discoveryHint);
        }
    }

    public static bool TryParseConnectionPayload(string payload, out PairingQrConnectionPayload? connectionPayload)
    {
        connectionPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        if (PairingCodeGenerator.TryParse(payload, out connectionPayload))
        {
            return IsValidConnectionPayload(connectionPayload, requireDiscoveryHostAddress: true);
        }

        try
        {
            connectionPayload = JsonSerializer.Deserialize<PairingQrConnectionPayload>(payload, PairingJson.Compact);
            if (IsValidConnectionPayload(connectionPayload, requireDiscoveryHostAddress: false))
            {
                return true;
            }

            if (TryParseMinifiedConnectionPayload(payload, out connectionPayload))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return TryParseMinifiedConnectionPayload(payload, out connectionPayload);
        }
    }

    private static bool IsValidDiscoveryHint(PairingDiscoveryHint? discoveryHint)
    {
        return discoveryHint is not null &&
               string.Equals(discoveryHint.Schema, PairingDiscoveryHint.SchemaName, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(discoveryHint.HostAddress);
    }

    private static bool IsValidConnectionPayload(
        PairingQrConnectionPayload? connectionPayload,
        bool requireDiscoveryHostAddress)
    {
        if (connectionPayload?.Connection is null ||
            !string.Equals(connectionPayload.Schema, PairingQrConnectionPayload.SchemaName, StringComparison.Ordinal) ||
            !string.Equals(connectionPayload.Connection.Schema, PairingConnectionHint.SchemaName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(connectionPayload.Connection.ConfigId) ||
            string.IsNullOrWhiteSpace(connectionPayload.Connection.OneTimeToken))
        {
            return false;
        }

        return !requireDiscoveryHostAddress || !string.IsNullOrWhiteSpace(connectionPayload.Discovery?.HostAddress);
    }

    private static PairingQrConnectionPayload CreateConnectionPayload(PairingConfig config, PairingDiscoveryHint discoveryHint)
    {
        return new PairingQrConnectionPayload
        {
            Schema = PairingQrConnectionPayload.SchemaName,
            Connection = CreateConnectionHint(config, discoveryHint.Source),
            Discovery = Create(discoveryHint)
        };
    }

    private const string schemaName = PairingDiscoveryHint.SchemaName;
}
