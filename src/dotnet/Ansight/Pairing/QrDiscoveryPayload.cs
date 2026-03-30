using System.Text.Json;

namespace Ansight.Pairing;

/// <summary>
/// Creates, serializes, and parses discovery and QR connection payloads for the pairing flow.
/// </summary>
public static partial class QrDiscoveryPayload
{
    /// <summary>
    /// Schema emitted for standalone discovery hint payloads.
    /// </summary>
    public const string Schema = PairingDiscoveryHint.SchemaName;

    /// <summary>
    /// Normalizes a discovery hint so it is ready for serialization.
    /// </summary>
    /// <param name="discoveryHint">Discovery hint to normalize.</param>
    /// <returns>The normalized discovery hint instance.</returns>
    public static PairingDiscoveryHint Create(PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(discoveryHint);

        discoveryHint.Schema = schemaName;
        return PairingDiscoveryHintHostAddresses.NormalizeInPlace(discoveryHint);
    }

    /// <summary>
    /// Serializes a standalone discovery hint payload.
    /// </summary>
    /// <param name="discoveryHint">Discovery hint to serialize.</param>
    /// <param name="indented"><see langword="true"/> to format the JSON with indentation.</param>
    /// <returns>Serialized discovery hint JSON.</returns>
    public static string Serialize(PairingDiscoveryHint discoveryHint, bool indented = false)
    {
        var payload = Create(discoveryHint);
        return JsonSerializer.Serialize(payload, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    /// <summary>
    /// Serializes a full QR connection payload that includes both connection and discovery data.
    /// </summary>
    /// <param name="config">Signed pairing configuration to include.</param>
    /// <param name="discoveryHint">Discovery information to include.</param>
    /// <param name="indented"><see langword="true"/> to format the JSON with indentation.</param>
    /// <returns>Serialized QR connection payload JSON.</returns>
    public static string Serialize(PairingConfig config, PairingDiscoveryHint discoveryHint, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(config);

        var payload = CreateConnectionPayload(config, discoveryHint);

        return JsonSerializer.Serialize(payload, indented ? PairingJson.Pretty : PairingJson.Compact);
    }

    /// <summary>
    /// Serializes a full QR connection payload into the compact text format used by <see cref="PairingCodeGenerator"/>.
    /// </summary>
    /// <param name="config">Signed pairing configuration to include.</param>
    /// <param name="discoveryHint">Discovery information to include.</param>
    /// <returns>A compact pairing code string.</returns>
    public static string SerializeCompactCode(PairingConfig config, PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(discoveryHint);

        return PairingCodeGenerator.Serialize(CreateConnectionPayload(config, discoveryHint));
    }

    /// <summary>
    /// Creates the lightweight connection hint that a QR or bootstrap payload exposes to the client.
    /// </summary>
    /// <param name="config">Source pairing configuration.</param>
    /// <param name="source">Optional human-readable source label associated with the payload.</param>
    /// <returns>A connection hint derived from the pairing config.</returns>
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

    /// <summary>
    /// Attempts to parse either a standalone discovery payload or a full QR connection payload.
    /// </summary>
    /// <param name="payload">Payload text to parse.</param>
    /// <param name="discoveryHint">Parsed discovery hint when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the payload could be parsed into a valid discovery hint; otherwise, <see langword="false"/>.</returns>
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
            if (discoveryHint is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(discoveryHint);
            }

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

    /// <summary>
    /// Attempts to parse a full QR connection payload from JSON or compact-code text.
    /// </summary>
    /// <param name="payload">Payload text to parse.</param>
    /// <param name="connectionPayload">Parsed connection payload when parsing succeeds.</param>
    /// <returns><see langword="true"/> when a valid QR connection payload was parsed; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseConnectionPayload(string payload, out PairingQrConnectionPayload? connectionPayload)
    {
        connectionPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        if (PairingCodeGenerator.TryParse(payload, out connectionPayload))
        {
            NormalizeConnectionPayload(connectionPayload);
            return IsValidConnectionPayload(connectionPayload, requireDiscoveryHostAddress: true);
        }

        try
        {
            connectionPayload = JsonSerializer.Deserialize<PairingQrConnectionPayload>(payload, PairingJson.Compact);
            NormalizeConnectionPayload(connectionPayload);
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
               !string.IsNullOrWhiteSpace(PairingDiscoveryHintHostAddresses.ResolvePrimary(discoveryHint));
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

        return !requireDiscoveryHostAddress ||
               !string.IsNullOrWhiteSpace(PairingDiscoveryHintHostAddresses.ResolvePrimary(connectionPayload.Discovery));
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

    private static void NormalizeConnectionPayload(PairingQrConnectionPayload? connectionPayload)
    {
        if (connectionPayload?.Discovery is not null)
        {
            PairingDiscoveryHintHostAddresses.NormalizeInPlace(connectionPayload.Discovery);
        }
    }

    private const string schemaName = PairingDiscoveryHint.SchemaName;
}
