using System.Text.Json;

namespace Ansight.Pairing;

public static partial class QrDiscoveryPayload
{
    private static readonly string[] sourcePropertyNames = ["source", "src"];
    private static readonly string[] connectionPropertyNames = ["connection", "c"];
    private static readonly string[] discoveryPropertyNames = ["discovery", "d"];
    private static readonly string[] challengePropertyNames = ["challenge", "ch"];
    private static readonly string[] configIdPropertyNames = ["configId", "ci", "cfg", "id"];
    private static readonly string[] issuedAtPropertyNames = ["issuedAt", "ia", "iat"];
    private static readonly string[] expiresAtPropertyNames = ["expiresAt", "ea", "exp"];
    private static readonly string[] oneTimeTokenPropertyNames = ["oneTimeToken", "ot", "token"];
    private static readonly string[] challengeAlgPropertyNames = ["alg", "a"];
    private static readonly string[] challengePubKeyPropertyNames = ["challengePubKey", "cpk", "pk", "pubKey"];
    private static readonly string[] requireProofPropertyNames = ["requireProofOnFirstPair", "requireProof", "proof", "rp"];
    private static readonly string[] hostAddressesPropertyNames = ["hostAddresses", "has", "ips", "addresses"];
    private static readonly string[] discoveryPortPropertyNames = ["discoveryPort", "dp", "port"];
    private static readonly string[] hostNamePropertyNames = ["hostName", "hn", "name"];
    private static readonly string[] wifiNamePropertyNames = ["wifiName", "wn", "wifi", "ssid"];
    private static readonly string[] capturedAtPropertyNames = ["capturedAt", "ca", "captured", "ts"];
    private static readonly string[] minifiedConnectionSignalPropertyNames = ["ci", "ia", "iat", "ea", "exp", "ot", "ch", "cpk", "pk", "rp"];
    private static readonly string[] minifiedDiscoverySignalPropertyNames = ["has", "ips", "dp", "port", "hn", "wn", "ca", "ts"];

    private static bool TryParseMinifiedConnectionPayload(string payload, out PairingQrConnectionPayload? connectionPayload)
    {
        connectionPayload = null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasConnectionWrapper = root.TryGetObjectPropertyValue(connectionPropertyNames, out var connectionElement);
            var hasDiscoveryWrapper = root.TryGetObjectPropertyValue(discoveryPropertyNames, out var discoveryElement);
            if (!hasConnectionWrapper &&
                !hasDiscoveryWrapper &&
                !LooksLikeMinifiedConnectionPayload(root))
            {
                return false;
            }

            var connectionCandidate = hasConnectionWrapper ? connectionElement : root;
            if (!TryParseConnectionHint(connectionCandidate, root, out var connection) || connection is null)
            {
                return false;
            }

            PairingDiscoveryHint? discovery = null;
            var discoveryCandidate = hasDiscoveryWrapper ? discoveryElement : root;
            if (TryParseOptionalDiscoveryHint(discoveryCandidate, root, connection.Source, out var parsedDiscovery))
            {
                discovery = parsedDiscovery;
                if (string.IsNullOrWhiteSpace(connection.Source) && !string.IsNullOrWhiteSpace(parsedDiscovery?.Source))
                {
                    connection.Source = parsedDiscovery.Source;
                }
            }

            connectionPayload = new PairingQrConnectionPayload
            {
                Schema = PairingQrConnectionPayload.SchemaName,
                Connection = connection,
                Discovery = discovery
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseMinifiedDiscoveryPayload(string payload, out PairingDiscoveryHint? discoveryHint)
    {
        discoveryHint = null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasDiscoveryWrapper = root.TryGetObjectPropertyValue(discoveryPropertyNames, out var discoveryElement);
            if (!hasDiscoveryWrapper &&
                !LooksLikeMinifiedDiscoveryPayload(root))
            {
                return false;
            }

            var discoveryCandidate = hasDiscoveryWrapper ? discoveryElement : root;
            return TryParseRequiredDiscoveryHint(discoveryCandidate, root, fallbackSource: null, out discoveryHint);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseConnectionHint(JsonElement candidate, JsonElement root, out PairingConnectionHint? connectionHint)
    {
        connectionHint = null;
        if (candidate.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!candidate.TryReadRequiredStringValue(configIdPropertyNames, out var configId) ||
            !candidate.TryReadRequiredDateTimeOffsetValue(issuedAtPropertyNames, out var issuedAt) ||
            !candidate.TryReadRequiredDateTimeOffsetValue(expiresAtPropertyNames, out var expiresAt) ||
            !candidate.TryReadRequiredStringValue(oneTimeTokenPropertyNames, out var oneTimeToken))
        {
            return false;
        }

        var challengeCandidate = candidate.TryGetObjectPropertyValue(challengePropertyNames, out var challengeElement)
            ? challengeElement
            : candidate;
        if (!challengeCandidate.TryReadRequiredStringValue(challengeAlgPropertyNames, out var challengeAlg) ||
            !challengeCandidate.TryReadRequiredStringValue(challengePubKeyPropertyNames, out var challengePubKey) ||
            !challengeCandidate.TryReadRequiredBooleanValue(requireProofPropertyNames, out var requireProofOnFirstPair))
        {
            return false;
        }

        candidate.TryReadOptionalStringValue(sourcePropertyNames, out var candidateSource);
        root.TryReadOptionalStringValue(sourcePropertyNames, out var rootSource);

        connectionHint = new PairingConnectionHint
        {
            Schema = PairingConnectionHint.SchemaName,
            Source = FirstNonEmpty(candidateSource, rootSource),
            ConfigId = configId,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            OneTimeToken = oneTimeToken,
            Challenge = new PairingChallenge
            {
                Alg = challengeAlg,
                ChallengePubKey = challengePubKey,
                RequireProofOnFirstPair = requireProofOnFirstPair
            }
        };

        return true;
    }

    private static bool TryParseRequiredDiscoveryHint(
        JsonElement candidate,
        JsonElement root,
        string? fallbackSource,
        out PairingDiscoveryHint? discoveryHint)
    {
        discoveryHint = null;
        if (!TryParseOptionalDiscoveryHint(candidate, root, fallbackSource, out var parsedDiscoveryHint) ||
            parsedDiscoveryHint is null ||
            string.IsNullOrWhiteSpace(PairingDiscoveryHintHostAddresses.ResolvePrimary(parsedDiscoveryHint)))
        {
            return false;
        }

        discoveryHint = parsedDiscoveryHint;
        return true;
    }

    private static bool TryParseOptionalDiscoveryHint(
        JsonElement candidate,
        JsonElement root,
        string? fallbackSource,
        out PairingDiscoveryHint? discoveryHint)
    {
        discoveryHint = null;
        if (candidate.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!candidate.TryReadOptionalStringArrayValue(hostAddressesPropertyNames, out var hostAddresses) ||
            !candidate.TryReadOptionalInt32Value(discoveryPortPropertyNames, out var discoveryPort) ||
            !candidate.TryReadOptionalStringValue(hostNamePropertyNames, out var hostName) ||
            !candidate.TryReadOptionalStringValue(wifiNamePropertyNames, out var wifiName) ||
            !candidate.TryReadOptionalDateTimeOffsetValue(capturedAtPropertyNames, out var capturedAt) ||
            !candidate.TryReadOptionalStringValue(sourcePropertyNames, out var candidateSource) ||
            !root.TryReadOptionalStringValue(sourcePropertyNames, out var rootSource))
        {
            return false;
        }

        var source = FirstNonEmpty(candidateSource, rootSource, fallbackSource);
        var normalizedHostAddresses = PairingDiscoveryHintHostAddresses.Normalize(hostAddresses);
        if (normalizedHostAddresses.Length == 0 &&
            string.IsNullOrWhiteSpace(hostName) &&
            string.IsNullOrWhiteSpace(wifiName) &&
            capturedAt is null &&
            string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        discoveryHint = new PairingDiscoveryHint
        {
            Schema = PairingDiscoveryHint.SchemaName,
            Source = source,
            HostAddresses = normalizedHostAddresses.Length == 0 ? null : normalizedHostAddresses,
            DiscoveryPort = discoveryPort,
            HostName = hostName,
            WifiName = wifiName,
            CapturedAt = capturedAt
        };

        return true;
    }

    private static bool LooksLikeMinifiedConnectionPayload(JsonElement element)
    {
        return element.HasAnyProperty(minifiedConnectionSignalPropertyNames) ||
               element.HasAnyProperty(minifiedDiscoverySignalPropertyNames);
    }

    private static bool LooksLikeMinifiedDiscoveryPayload(JsonElement element)
    {
        return element.HasAnyProperty(minifiedDiscoverySignalPropertyNames);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
