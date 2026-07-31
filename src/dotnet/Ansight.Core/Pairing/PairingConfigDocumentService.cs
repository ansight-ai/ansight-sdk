using System.Security.Cryptography;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingConfigDocumentService
{
    public bool TryParseAndValidateConfigDocument(
        string payload,
        string? expectedAppId,
        out PairingConfigDocument? configDocument,
        out string error)
    {
        configDocument = null;
        if (!TryParseConfigDocument(payload, out configDocument, out error)
            || configDocument is null)
        {
            return false;
        }

        if (!TryValidateConfigDocument(configDocument, expectedAppId, out error))
        {
            configDocument = null;
            return false;
        }

        return true;
    }

    public bool TryValidateConfigDocument(
        PairingConfigDocument configDocument,
        string? expectedAppId,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(configDocument);
        if (!string.Equals(
                configDocument.Schema,
                PairingConfigDocument.SchemaName,
                StringComparison.Ordinal))
        {
            error = $"Unsupported enrollment invite schema '{configDocument.Schema}'.";
            return false;
        }

        return TryValidateConfig(configDocument.Config, expectedAppId, out error);
    }

    public bool TryParseConfigDocument(
        string payload,
        out PairingConfigDocument? configDocument,
        out string error)
    {
        configDocument = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "Scan an Ansight enrollment QR code.";
            return false;
        }

        if (PairingConfigCodeGenerator.TryParse(payload, out configDocument)
            && configDocument is not null)
        {
            NormalizeDiscovery(configDocument);
            error = string.Empty;
            return true;
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Enrollment invite JSON must be an object.";
                return false;
            }

            var schema = GetSchema(root);
            if (string.Equals(schema, PairingConfig.SchemaName, StringComparison.Ordinal))
            {
                var invite = JsonSerializer.Deserialize<PairingConfig>(payload, PairingJson.Compact);
                if (invite is null)
                {
                    error = "Enrollment invite could not be parsed.";
                    return false;
                }

                configDocument = CreateConfigDocument(invite);
                error = string.Empty;
                return true;
            }

            if (!string.Equals(
                    schema,
                    PairingConfigDocument.SchemaName,
                    StringComparison.Ordinal))
            {
                error = string.IsNullOrWhiteSpace(schema)
                    ? "The QR code is not an Ansight enrollment invite."
                    : $"Unsupported enrollment invite schema '{schema}'.";
                return false;
            }

            configDocument =
                JsonSerializer.Deserialize<PairingConfigDocument>(payload, PairingJson.Compact);
            if (configDocument?.Config is null)
            {
                error = "Enrollment invite document is missing its invite.";
                configDocument = null;
                return false;
            }

            NormalizeDiscovery(configDocument);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse enrollment invite: {ex.Message}";
            return false;
        }
    }

    public bool TryParseAndValidateDocument(
        string configJson,
        string? expectedAppId,
        out ParsedPairingDocument? document,
        out string error)
    {
        document = null;
        if (!TryParseConfigDocument(configJson, out var configDocument, out error)
            || configDocument is null
            || !TryValidateConfigDocument(configDocument, expectedAppId, out error))
        {
            return false;
        }

        document = CreateDocument(configDocument);
        return true;
    }

    public bool TryParseAndValidateConfig(
        string configJson,
        string? expectedAppId,
        out PairingConfig? config,
        out string error)
    {
        config = null;
        if (!TryParseAndValidateDocument(
                configJson,
                expectedAppId,
                out var document,
                out error))
        {
            return false;
        }

        config = document!.Config;
        return true;
    }

    public bool TryValidateConfig(
        PairingConfig config,
        string? expectedAppId,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.Equals(config.Schema, PairingConfig.SchemaName, StringComparison.Ordinal))
        {
            error = $"Unsupported enrollment invite schema '{config.Schema}'.";
            return false;
        }

        if (config.MinProtocolVersion != 2
            || config.AllowedTransports is not ["ws"]
            || string.IsNullOrWhiteSpace(config.ConfigId)
            || string.IsNullOrWhiteSpace(config.AppId)
            || string.IsNullOrWhiteSpace(config.AppName)
            || config.Host is null
            || config.Host.DiscoveryPort is <= 0 or > ushort.MaxValue
            || config.Enrollment is null
            || config.Enrollment.MaxUses != 1
            || !IsBase64UrlByteCount(config.Enrollment.Secret, 32))
        {
            error = "Enrollment invite is incomplete or uses an unsupported connection protocol.";
            return false;
        }

        if (config.Enrollment.GrantExpiresAt < DateTimeOffset.UtcNow)
        {
            error = $"Device registration expired at {config.Enrollment.GrantExpiresAt:O}. Scan a fresh QR code.";
            return false;
        }

        var normalizedExpected = expectedAppId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedExpected)
            && !string.Equals(config.AppId.Trim(), PairingConfig.AnyAppId, StringComparison.Ordinal)
            && !string.Equals(config.AppId.Trim(), normalizedExpected, StringComparison.Ordinal))
        {
            error =
                $"Enrollment invite appId '{config.AppId}' does not match expected app id '{normalizedExpected}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateDocument(
        ParsedPairingDocument document,
        string? expectedAppId,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(document);
        return TryValidateConfig(document.Config, expectedAppId, out error);
    }

    public bool TryParseDocument(
        string configJson,
        out ParsedPairingDocument? document,
        out string error)
    {
        document = null;
        if (!TryParseConfigDocument(configJson, out var configDocument, out error)
            || configDocument is null)
        {
            return false;
        }

        document = CreateDocument(configDocument);
        return true;
    }

    internal static ParsedPairingDocument CreateDocument(
        PairingConfigDocument configDocument)
    {
        ArgumentNullException.ThrowIfNull(configDocument);
        return new ParsedPairingDocument
        {
            Config = configDocument.Config,
            DiscoveryHint = configDocument.Discovery is null
                ? null
                : CloneDiscovery(configDocument.Discovery)
        };
    }

    internal static ParsedPairingDocument CreateDocument(PairingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new ParsedPairingDocument { Config = config };
    }

    internal static PairingConfigDocument CreateConfigDocument(PairingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new PairingConfigDocument
        {
            Schema = PairingConfigDocument.SchemaName,
            Config = config
        };
    }

    internal static PairingConfigDocument CreateConfigDocument(
        ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new PairingConfigDocument
        {
            Schema = PairingConfigDocument.SchemaName,
            Config = document.Config,
            Discovery = document.DiscoveryHint is null
                ? null
                : CloneDiscovery(document.DiscoveryHint)
        };
    }

    private static void NormalizeDiscovery(PairingConfigDocument document)
    {
        if (document.Discovery is not null)
        {
            PairingDiscoveryHintHostAddresses.NormalizeInPlace(document.Discovery);
        }
    }

    private static string? GetSchema(JsonElement root)
        => root.TryGetProperty("schema", out var schema)
            ? schema.GetString()
            : null;

    private static bool IsBase64UrlByteCount(string? value, int byteCount)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var bytes = PairingCrypto.FromBase64Url(value);
            try
            {
                return bytes.Length == byteCount;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static PairingDiscoveryHint CloneDiscovery(PairingDiscoveryHint discovery)
    {
        PairingDiscoveryHintHostAddresses.NormalizeInPlace(discovery);
        return new PairingDiscoveryHint
        {
            Schema = string.IsNullOrWhiteSpace(discovery.Schema)
                ? PairingDiscoveryHint.SchemaName
                : discovery.Schema,
            Source = discovery.Source,
            HostAddresses = discovery.HostAddresses is null
                ? null
                : [.. discovery.HostAddresses],
            DiscoveryPort = discovery.DiscoveryPort,
            HostName = discovery.HostName,
            WifiName = discovery.WifiName,
            CapturedAt = discovery.CapturedAt
        };
    }
}
