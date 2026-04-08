using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingConfigDocumentService
{
    public bool TryParseAndValidateTicket(string payload, string? expectedAppId, out PairingTicket? ticket, out string error)
    {
        ticket = null;

        if (!TryParseTicket(payload, out ticket, out error) || ticket is null)
        {
            return false;
        }

        if (!TryValidateTicket(ticket, expectedAppId, out error))
        {
            ticket = null;
            return false;
        }

        return true;
    }

    public bool TryValidateTicket(PairingTicket ticket, string? expectedAppId, out string error)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (!string.Equals(ticket.Schema, PairingTicket.SchemaName, StringComparison.Ordinal))
        {
            error = $"Unsupported pairing ticket schema '{ticket.Schema}'.";
            return false;
        }

        return TryValidateConfig(ticket.Config, expectedAppId, out error);
    }

    public bool TryParseTicket(string payload, out PairingTicket? ticket, out string error)
    {
        ticket = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "Paste or load a pairing ticket.";
            return false;
        }

        if (PairingTicketCodeGenerator.TryParse(payload, out ticket) && ticket is not null)
        {
            if (ticket.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(ticket.Discovery);
            }

            error = string.Empty;
            return true;
        }

        try
        {
            var parsedTicket = JsonSerializer.Deserialize<PairingTicket>(payload, PairingJson.Compact);
            if (parsedTicket?.Config is null)
            {
                error = "Pairing ticket did not contain a pairing config.";
                return false;
            }

            if (!string.Equals(parsedTicket.Schema, PairingTicket.SchemaName, StringComparison.Ordinal))
            {
                error = $"Unsupported pairing ticket schema '{parsedTicket.Schema}'.";
                return false;
            }

            if (parsedTicket.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(parsedTicket.Discovery);
            }

            ticket = parsedTicket;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse pairing ticket: {ex.Message}";
            return false;
        }
    }

    public bool TryParseAndValidateDocument(string configJson, string? expectedAppId, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(configJson))
        {
            error = "Paste or load a pairing ticket.";
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

        return TryValidateConfig(document.Config, expectedAppId, out error);
    }

    public bool TryParseDocument(string configJson, out ParsedPairingDocument? document, out string error)
    {
        document = null;

        if (PairingTicketCodeGenerator.TryParse(configJson, out var compactTicket) && compactTicket is not null)
        {
            if (compactTicket.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(compactTicket.Discovery);
            }

            document = CreateDocument(compactTicket);
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

            var schema = root.TryGetProperty("schema", out var schemaElement)
                ? schemaElement.GetString()
                : null;

            if (string.Equals(schema, "ansight.pairing-bootstrap.v1", StringComparison.Ordinal))
            {
                error = "Legacy bootstrap pairing payloads are no longer supported. Export a fresh pairing ticket from Ansight Studio.";
                return false;
            }

            if (!string.Equals(schema, PairingTicket.SchemaName, StringComparison.Ordinal))
            {
                error = string.IsNullOrWhiteSpace(schema)
                    ? "Pairing payloads must be pairing tickets."
                    : $"Unsupported pairing payload schema '{schema}'. Export a fresh pairing ticket from Ansight Studio.";
                return false;
            }

            var ticket = JsonSerializer.Deserialize<PairingTicket>(configJson, PairingJson.Compact);
            if (ticket?.Config is null)
            {
                error = "Pairing ticket did not contain a pairing config.";
                return false;
            }

            if (ticket.Discovery is not null)
            {
                PairingDiscoveryHintHostAddresses.NormalizeInPlace(ticket.Discovery);
            }

            document = CreateDocument(ticket);

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse pairing ticket: {ex.Message}";
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

            var signable = PairingCanonicalJson.SerializePairingConfigForSignature(config);
            return hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256);
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
            error = $"Pairing ticket appId '{configuredAppId}' does not match expected app id '{normalizedExpected}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static ParsedPairingDocument CreateDocument(PairingTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return new ParsedPairingDocument
        {
            Config = ticket.Config,
            DiscoveryHint = ticket.Discovery is null ? null : CloneDiscovery(ticket.Discovery)
        };
    }

    internal static PairingTicket CreateTicket(ParsedPairingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new PairingTicket
        {
            Schema = PairingTicket.SchemaName,
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
