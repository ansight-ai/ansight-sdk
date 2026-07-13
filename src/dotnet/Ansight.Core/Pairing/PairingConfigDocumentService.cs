using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingConfigDocumentService
{
    private readonly PairingV2Validator v2Validator;

    public PairingConfigDocumentService(PairingV2ValidationPolicy? v2Policy = null)
    {
        v2Validator = new PairingV2Validator(v2Policy);
    }

    public bool TryParseAndValidateConfigDocument(string payload, string? expectedAppId, out PairingConfigDocument? configDocument, out string error)
    {
        configDocument = null;

        if (!TryParseConfigDocument(payload, out configDocument, out error) || configDocument is null)
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

    public bool TryValidateConfigDocument(PairingConfigDocument configDocument, string? expectedAppId, out string error)
    {
        ArgumentNullException.ThrowIfNull(configDocument);

        if (!IsSupportedConfigDocumentSchema(configDocument.Schema))
        {
            error = $"Unsupported pairing config schema '{configDocument.Schema}'.";
            return false;
        }

        return TryValidateConfig(configDocument.Config, expectedAppId, out error);
    }

    public bool TryParseConfigDocument(string payload, out PairingConfigDocument? configDocument, out string error)
    {
        configDocument = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "Paste or load a pairing config.";
            return false;
        }

        if (PairingConfigCodeGenerator.TryParse(payload, out configDocument) && configDocument is not null)
        {
            if (configDocument.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(configDocument.Discovery);
            }

            error = string.Empty;
            return true;
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Config JSON root must be an object.";
                return false;
            }

            var schema = GetSchema(root);
            if (IsSupportedConfigSchema(schema))
            {
                var parsedConfig = JsonSerializer.Deserialize<PairingConfig>(payload, PairingJson.Compact);
                if (parsedConfig is null)
                {
                    error = "Pairing config payload could not be parsed.";
                    return false;
                }

                configDocument = CreateConfigDocument(parsedConfig);
                error = string.Empty;
                return true;
            }

            var parsedConfigDocument = JsonSerializer.Deserialize<PairingConfigDocument>(payload, PairingJson.Compact);
            if (parsedConfigDocument?.Config is null)
            {
                error = "Pairing config document did not contain a pairing config.";
                return false;
            }

            if (!IsSupportedConfigDocumentSchema(parsedConfigDocument.Schema))
            {
                error = $"Unsupported pairing config schema '{parsedConfigDocument.Schema}'.";
                return false;
            }

            if (parsedConfigDocument.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(parsedConfigDocument.Discovery);
            }

            configDocument = parsedConfigDocument;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse pairing config: {ex.Message}";
            return false;
        }
    }

    public bool TryParseAndValidateDocument(string configJson, string? expectedAppId, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(configJson))
        {
            error = "Paste or load a pairing config.";
            return false;
        }

        if (!TryParseDocument(configJson, out document, out error))
        {
            return false;
        }

        if (document is null)
        {
            error = "Pairing document could not be parsed.";
            return false;
        }

        if (!TryValidateDocument(document, expectedAppId, out error))
        {
            document = null;
            return false;
        }

        return true;
    }

    public bool TryParseAndValidateConfig(string configJson, string? expectedAppId, out PairingConfig? config, out string error)
    {
        config = null;
        if (!TryParseAndValidateDocument(configJson, expectedAppId, out var document, out error))
        {
            return false;
        }

        config = document!.Config;
        return true;
    }

    public bool TryValidateConfig(PairingConfig config, string? expectedAppId, out string error)
    {
        if (!VerifyPairingConfigSignature(config))
        {
            error = "Connection config signature is invalid.";
            return false;
        }

        if (DateTimeOffset.UtcNow > config.ExpiresAt)
        {
            error = $"Connection config expired at {config.ExpiresAt:O}.";
            return false;
        }

        if (!ValidateAppId(config, expectedAppId, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateDocument(ParsedPairingDocument document, string? expectedAppId, out string error)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SecureConfig is not null)
        {
            if (document.IsRememberedSecureV2)
            {
                return v2Validator.TryValidateReconnectConfig(
                    document.SecureConfig,
                    expectedAppId ?? document.SecureConfig.AppId,
                    DateTimeOffset.UtcNow,
                    out error);
            }

            return v2Validator.TryValidateConfig(document.SecureConfig, expectedAppId, DateTimeOffset.UtcNow, out error);
        }

        if (document.Config is null)
        {
            error = "Pairing document did not contain a pairing config.";
            return false;
        }

        return TryValidateConfig(document.Config, expectedAppId, out error);
    }

    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        if (PairingConfigCodeGenerator.TryParse(configJson, out var compactConfigDocument) && compactConfigDocument is not null)
        {
            if (compactConfigDocument.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(compactConfigDocument.Discovery);
            }

            document = CreateDocument(compactConfigDocument);
            error = string.Empty;
            return true;
        }

        try
        {
            using var json = JsonDocument.Parse(configJson);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Config JSON root must be an object.";
                return false;
            }

            var schema = GetSchema(root);

            if (string.Equals(schema, "ansight.pairing-bootstrap.v1", StringComparison.Ordinal))
            {
                error = "Legacy bootstrap pairing payloads are no longer supported. Export a fresh pairing config from Ansight host.";
                return false;
            }

            if (IsSupportedConfigSchema(schema))
            {
                var parsedConfig = JsonSerializer.Deserialize<PairingConfig>(configJson, PairingJson.Compact);
                if (parsedConfig is null)
                {
                    error = "Pairing config payload could not be parsed.";
                    return false;
                }

                document = CreateDocument(parsedConfig);
                error = string.Empty;
                return true;
            }

            if (IsSupportedSecureConfigSchema(schema))
            {
                var parsedConfig = JsonSerializer.Deserialize<PairingConfigV2>(configJson, PairingJson.Compact);
                if (parsedConfig is null)
                {
                    error = "Secure pairing config payload could not be parsed.";
                    return false;
                }

                document = CreateDocument(parsedConfig);
                error = string.Empty;
                return true;
            }

            if (IsSupportedSecureConfigDocumentSchema(schema))
            {
                var secureDocument = JsonSerializer.Deserialize<PairingConfigDocumentV2>(configJson, PairingJson.Compact);
                if (secureDocument?.Config is null)
                {
                    error = "Secure pairing config document did not contain a pairing config.";
                    return false;
                }

                if (secureDocument.Discovery is not null)
                {
                    PairingDiscoveryHintHostAddresses.NormalizeInPlace(secureDocument.Discovery);
                }

                document = CreateDocument(secureDocument);
                error = string.Empty;
                return true;
            }

            if (!IsSupportedConfigDocumentSchema(schema))
            {
                error = string.IsNullOrWhiteSpace(schema)
                    ? "Pairing payloads must be pairing configs."
                    : $"Unsupported pairing payload schema '{schema}'. Export a fresh pairing config from Ansight host.";
                return false;
            }

            var configDocument = JsonSerializer.Deserialize<PairingConfigDocument>(configJson, PairingJson.Compact);
            if (configDocument?.Config is null)
            {
                error = "Pairing config document did not contain a pairing config.";
                return false;
            }

            if (configDocument.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(configDocument.Discovery);
            }

            document = CreateDocument(configDocument);

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse pairing config: {ex.Message}";
            return false;
        }
    }

    private static string? GetSchema(JsonElement root)
    {
        return root.TryGetProperty("schema", out var schemaElement)
            ? schemaElement.GetString()
            : null;
    }

    private static bool IsSupportedConfigSchema(string? schema)
    {
        return string.Equals(schema, PairingConfig.SchemaName, StringComparison.Ordinal);
    }

    private static bool IsSupportedSecureConfigSchema(string? schema)
        => string.Equals(schema, PairingConfigV2.SchemaName, StringComparison.Ordinal);

    private static bool IsSupportedSecureConfigDocumentSchema(string? schema)
        => string.Equals(schema, PairingConfigDocumentV2.SchemaName, StringComparison.Ordinal);

    private static bool IsSupportedConfigDocumentSchema(string? schema)
    {
        return string.Equals(schema, PairingConfigDocument.SchemaName, StringComparison.Ordinal) ||
               string.Equals(schema, PairingConfigDocument.LegacySchemaName, StringComparison.Ordinal);
    }

    private static bool VerifyPairingConfigSignature(PairingConfig config)
    {
        try
        {
            var publicKey = Convert.FromBase64String(config.Host.HostPubKey);
            var signature = Convert.FromBase64String(config.Signature);

            using var hostKey = ECDsa.Create();
            hostKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            return VerifySignature(hostKey, signature, PairingCanonicalJson.SerializePairingConfigForSignature(config))
                   || VerifySignature(hostKey, signature, PairingCanonicalJson.SerializePairingConfigWithLegacyTrustForSignature(config));
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifySignature(ECDsa hostKey, byte[] signature, string signable)
    {
        return hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256);
    }

    private static bool ValidateAppId(PairingConfig config, string? expectedAppId, out string error)
    {
        var configuredAppId = config.AppId?.Trim() ?? string.Empty;
        var normalizedExpected = expectedAppId?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            error = string.Empty;
            return true;
        }

        if (!string.Equals(configuredAppId, normalizedExpected, StringComparison.Ordinal))
        {
            error = $"Pairing config appId '{configuredAppId}' does not match expected app id '{normalizedExpected}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static ParsedPairingDocument CreateDocument(PairingConfigDocument configDocument)
    {
        ArgumentNullException.ThrowIfNull(configDocument);

        return new ParsedPairingDocument
        {
            Config = configDocument.Config,
            DiscoveryHint = configDocument.Discovery is null ? null : CloneDiscovery(configDocument.Discovery)
        };
    }

    internal static ParsedPairingDocument CreateDocument(PairingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new ParsedPairingDocument
        {
            Config = config
        };
    }

    internal static ParsedPairingDocument CreateDocument(PairingConfigV2 config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new ParsedPairingDocument
        {
            SecureConfig = config
        };
    }

    internal static ParsedPairingDocument CreateDocument(PairingConfigDocumentV2 configDocument)
    {
        ArgumentNullException.ThrowIfNull(configDocument);

        return new ParsedPairingDocument
        {
            SecureConfig = configDocument.Config,
            DiscoveryHint = configDocument.Discovery is null ? null : CloneDiscovery(configDocument.Discovery)
        };
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

    internal static PairingConfigDocument CreateConfigDocument(ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Config is null)
        {
            throw new InvalidOperationException("Protocol-v2 documents cannot be written to the legacy config store.");
        }

        return new PairingConfigDocument
        {
            Schema = PairingConfigDocument.SchemaName,
            Config = document.Config,
            Discovery = document.DiscoveryHint is null ? null : CloneDiscovery(document.DiscoveryHint)
        };
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
            HostAddresses = discovery.HostAddresses is null ? null : [.. discovery.HostAddresses],
            DiscoveryPort = discovery.DiscoveryPort,
            HostName = discovery.HostName,
            WifiName = discovery.WifiName,
            CapturedAt = discovery.CapturedAt
        };
    }
}
