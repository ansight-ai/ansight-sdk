using System.Globalization;
using Ansight.Pairing.Models;

namespace Ansight.Pairing;

public static class PairingCodeGenerator
{
    public const string FormatPrefix = "apc1";
    private const string MinifiedFormatPrefix = "apm1";

    public static string Serialize(PairingConfig config, PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(discoveryHint);

        return Serialize(new PairingQrConnectionPayload
        {
            Schema = PairingQrConnectionPayload.SchemaName,
            Connection = QrDiscoveryPayload.CreateConnectionHint(config, discoveryHint.Source),
            Discovery = QrDiscoveryPayload.Create(discoveryHint)
        });
    }

    public static string Serialize(PairingQrConnectionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payload.Connection);
        ArgumentNullException.ThrowIfNull(payload.Connection.Challenge);

        var discovery = payload.Discovery;
        var source = payload.Connection.Source ?? discovery?.Source;
        var hostAddresses = PairingDiscoveryHintHostAddresses.Normalize(discovery);
        var lines = new List<string>
        {
            FormatPrefix,
            Escape(payload.Connection.ConfigId),
            payload.Connection.IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            payload.Connection.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Escape(payload.Connection.OneTimeToken),
            Escape(payload.Connection.Challenge.Alg),
            payload.Connection.Challenge.RequireProofOnFirstPair ? "1" : "0",
            Escape(payload.Connection.Challenge.ChallengePubKey),
            EscapeHostAddresses(hostAddresses),
            Escape(discovery?.HostName),
            Escape(discovery?.WifiName),
            discovery?.CapturedAt is DateTimeOffset capturedAt
                ? capturedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            Escape(source)
        };

        TrimTrailingEmptyLines(lines);
        return string.Join('\n', lines);
    }

    public static bool TryParse(string payload, out PairingQrConnectionPayload? connectionPayload)
    {
        connectionPayload = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var normalizedPayload = payload.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var fields = normalizedPayload.Split('\n').ToList();
        TrimTrailingEmptyLines(fields);
        if (fields.Count < 9 || !IsSupportedFormatPrefix(fields[0]))
        {
            return false;
        }

        if (!TryUnescapeRequired(fields, 1, out var configId)
            || !TryParseUnixTimeSeconds(fields, 2, out var issuedAt)
            || !TryParseUnixTimeSeconds(fields, 3, out var expiresAt)
            || !TryUnescapeRequired(fields, 4, out var oneTimeToken)
            || !TryUnescapeRequired(fields, 5, out var challengeAlg)
            || !TryParseProofFlag(fields, 6, out var requireProofOnFirstPair)
            || !TryUnescapeRequired(fields, 7, out var challengePubKey)
            || !TryUnescapeRequired(fields, 8, out var rawHostAddresses))
        {
            return false;
        }

        if (!TryUnescapeOptional(fields, 9, out var hostName)
            || !TryUnescapeOptional(fields, 10, out var wifiName)
            || !TryParseOptionalUnixTimeSeconds(fields, 11, out var capturedAt)
            || !TryUnescapeOptional(fields, 12, out var source))
        {
            return false;
        }

        var hostAddresses = ParseHostAddresses(rawHostAddresses);

        connectionPayload = new PairingQrConnectionPayload
        {
            Schema = PairingQrConnectionPayload.SchemaName,
            Connection = new PairingConnectionHint
            {
                Schema = PairingConnectionHint.SchemaName,
                Source = source,
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
            },
            Discovery = new PairingDiscoveryHint
            {
                Schema = PairingDiscoveryHint.SchemaName,
                Source = source,
                HostAddresses = hostAddresses.Length == 0 ? null : hostAddresses,
                HostName = hostName,
                WifiName = wifiName,
                CapturedAt = capturedAt
            }
        };

        return true;
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);
    }

    private static string EscapeHostAddresses(IReadOnlyList<string> hostAddresses)
    {
        return hostAddresses.Count == 0
            ? string.Empty
            : string.Join('|', hostAddresses.Select(Escape));
    }

    private static string[] ParseHostAddresses(string? rawHostAddresses)
    {
        var parsedHostAddresses = string.IsNullOrWhiteSpace(rawHostAddresses)
            ? Array.Empty<string>()
            : rawHostAddresses.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return PairingDiscoveryHintHostAddresses.Normalize(parsedHostAddresses);
    }

    private static bool TryUnescapeRequired(IReadOnlyList<string> fields, int index, out string value)
    {
        if (!TryUnescapeOptional(fields, index, out var optionalValue) || string.IsNullOrWhiteSpace(optionalValue))
        {
            value = string.Empty;
            return false;
        }

        value = optionalValue;
        return true;
    }

    private static bool TryUnescapeOptional(IReadOnlyList<string> fields, int index, out string? value)
    {
        value = null;
        if (index >= fields.Count)
        {
            return true;
        }

        var raw = fields[index];
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        return TryUnescape(raw, out value);
    }

    private static bool TryUnescape(string raw, out string? value)
    {
        var builder = new System.Text.StringBuilder(raw.Length);
        for (var index = 0; index < raw.Length; index++)
        {
            var current = raw[index];
            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (index + 1 >= raw.Length)
            {
                value = null;
                return false;
            }

            index++;
            switch (raw[index])
            {
                case '\\':
                    builder.Append('\\');
                    break;

                case 'n':
                    builder.Append('\n');
                    break;

                case 'r':
                    builder.Append('\r');
                    break;

                default:
                    value = null;
                    return false;
            }
        }

        value = builder.ToString();
        return true;
    }

    private static bool TryParseUnixTimeSeconds(IReadOnlyList<string> fields, int index, out DateTimeOffset value)
    {
        value = default;
        if (index >= fields.Count
            || !long.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimeSeconds))
        {
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
        return true;
    }

    private static bool TryParseOptionalUnixTimeSeconds(IReadOnlyList<string> fields, int index, out DateTimeOffset? value)
    {
        value = null;
        if (index >= fields.Count || string.IsNullOrWhiteSpace(fields[index]))
        {
            return true;
        }

        if (!TryParseUnixTimeSeconds(fields, index, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParseProofFlag(IReadOnlyList<string> fields, int index, out bool value)
    {
        value = false;
        if (index >= fields.Count)
        {
            return false;
        }

        switch (fields[index])
        {
            case "1":
                value = true;
                return true;

            case "0":
                value = false;
                return true;

            default:
                return false;
        }
    }

    private static bool IsSupportedFormatPrefix(string value)
    {
        return string.Equals(value, FormatPrefix, StringComparison.Ordinal) ||
               string.Equals(value, MinifiedFormatPrefix, StringComparison.Ordinal);
    }

    private static void TrimTrailingEmptyLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }
}
