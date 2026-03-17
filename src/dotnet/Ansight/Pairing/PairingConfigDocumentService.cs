using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingConfigDocumentService
{
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

        return TryValidateDocument(document, expectedAppId, out error);
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

        var trustAnchorConfig = document.TrustAnchorConfig ?? document.Config;
        if (!VerifyPairingConfigSignature(trustAnchorConfig))
        {
            error = "Connection config signature is invalid.";
            return false;
        }

        if (DateTimeOffset.UtcNow > document.Config.ExpiresAt)
        {
            error = $"Connection config expired at {document.Config.ExpiresAt:O}.";
            return false;
        }

        if (!ValidateAppId(document.Config, expectedAppId, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        try
        {
            using var json = JsonDocument.Parse(configJson);
            var root = json.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Config JSON root must be an object.";
                return false;
            }

            var schema = root.TryGetProperty("schema", out var schemaElement)
                ? schemaElement.GetString()
                : null;

            if (string.Equals(schema, PairingBootstrapDocument.SchemaName, StringComparison.Ordinal))
            {
                var bootstrap = JsonSerializer.Deserialize<PairingBootstrapDocument>(configJson, PairingJson.Compact);
                if (bootstrap?.PairingConfig is null)
                {
                    error = "Bootstrap document did not contain a pairing config.";
                    return false;
                }

                var effectiveConfig = bootstrap.ConnectionHint is null
                    ? bootstrap.PairingConfig
                    : ApplyConnectionHint(bootstrap.PairingConfig, bootstrap.ConnectionHint);

                document = new ParsedPairingDocument
                {
                    Config = effectiveConfig,
                    DiscoveryHint = bootstrap.Discovery,
                    TrustAnchorConfig = bootstrap.ConnectionHint is null ? null : bootstrap.PairingConfig,
                    ConnectionHint = bootstrap.ConnectionHint
                };

                error = string.Empty;
                return true;
            }

            var config = JsonSerializer.Deserialize<PairingConfig>(configJson, PairingJson.Compact);
            if (config is null)
            {
                error = "Config JSON is empty.";
                return false;
            }

            document = new ParsedPairingDocument
            {
                Config = config,
                DiscoveryHint = null,
                TrustAnchorConfig = null,
                ConnectionHint = null
            };

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse config JSON: {ex.Message}";
            return false;
        }
    }

    private static bool VerifyPairingConfigSignature(PairingConfig config)
    {
        try
        {
            var publicKey = Convert.FromBase64String(config.Host.HostPubKey);
            var signature = Convert.FromBase64String(config.Signature);

            using var hostKey = ECDsa.Create();
            hostKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            var signables = new[]
            {
                PairingCanonicalJson.SerializePairingConfigForSignature(config),
                PairingCanonicalJson.SerializePairingConfigForSignatureWithoutHostIdentity(config),
                PairingCanonicalJson.SerializeTransportPairingConfigForSignature(config),
                PairingCanonicalJson.SerializeTransportPairingConfigForSignatureWithoutHostIdentity(config),
                PairingCanonicalJson.SerializeLegacyPairingConfigForSignature(config),
                PairingCanonicalJson.SerializeLegacyPairingConfigForSignatureWithoutHostIdentity(config)
            };

            foreach (var signable in signables)
            {
                if (hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
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
            error = $"Config appId '{configuredAppId}' does not match expected app id '{normalizedExpected}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static PairingConfig ApplyConnectionHint(PairingConfig trustAnchorConfig, PairingConnectionHint connectionHint)
    {
        return new PairingConfig
        {
            Schema = trustAnchorConfig.Schema,
            ConfigId = connectionHint.ConfigId,
            AppId = trustAnchorConfig.AppId,
            AppName = trustAnchorConfig.AppName,
            IssuedAt = connectionHint.IssuedAt,
            ExpiresAt = connectionHint.ExpiresAt,
            OneTimeToken = connectionHint.OneTimeToken,
            Host = new PairingHost
            {
                HostId = trustAnchorConfig.Host.HostId,
                HostName = trustAnchorConfig.Host.HostName,
                DiscoveryPort = trustAnchorConfig.Host.DiscoveryPort,
                HostPubKey = trustAnchorConfig.Host.HostPubKey,
                HostPubKeyFingerprint = trustAnchorConfig.Host.HostPubKeyFingerprint
            },
            Challenge = new PairingChallenge
            {
                Alg = connectionHint.Challenge.Alg,
                ChallengePubKey = connectionHint.Challenge.ChallengePubKey,
                RequireProofOnFirstPair = connectionHint.Challenge.RequireProofOnFirstPair
            },
            Trust = new PairingTrust
            {
                Mode = trustAnchorConfig.Trust.Mode,
                RequireTokenOnFirstPair = trustAnchorConfig.Trust.RequireTokenOnFirstPair,
                AllowLanDiscovery = trustAnchorConfig.Trust.AllowLanDiscovery
            },
            Signature = trustAnchorConfig.Signature
        };
    }
}
