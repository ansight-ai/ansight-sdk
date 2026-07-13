using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

internal static class PairingV2CanonicalJson
{
    private static readonly string[] scopeOrder = ["Read", "Write", "Delete"];

    public static string SerializeConfig(PairingConfigV2 config)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("schema", config.Schema);
            writer.WriteString("configId", config.ConfigId);
            writer.WriteString("appId", config.AppId);
            writer.WriteString("appName", config.AppName);
            writer.WriteString("issuedAt", config.IssuedAt);
            writer.WriteString("expiresAt", config.ExpiresAt);
            writer.WriteNumber("minProtocolVersion", config.MinProtocolVersion);
            WriteStringArray(writer, "allowedTransports", config.AllowedTransports);
            writer.WritePropertyName("host");
            writer.WriteStartObject();
            writer.WriteString("hostId", config.Host.HostId);
            writer.WriteString("hostName", config.Host.HostName);
            writer.WriteNumber("discoveryPort", config.Host.DiscoveryPort);
            writer.WriteString("hostPubKey", config.Host.HostPubKey);
            writer.WriteString("hostPubKeyFingerprint", config.Host.HostPubKeyFingerprint);
            writer.WritePropertyName("tlsPins");
            writer.WriteStartArray();
            foreach (var pin in config.Host.TlsPins
                         .OrderBy(pin => pin.NotBefore, StringComparer.Ordinal)
                         .ThenBy(pin => pin.TlsSpkiSha256, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("tlsSpkiSha256", pin.TlsSpkiSha256);
                writer.WriteString("notBefore", pin.NotBefore);
                writer.WriteString("notAfter", pin.NotAfter);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("enrollment");
            writer.WriteStartObject();
            writer.WriteString("ticketId", config.Enrollment.TicketId);
            writer.WriteString("secret", config.Enrollment.Secret);
            writer.WriteString("expiresAt", config.Enrollment.ExpiresAt);
            writer.WriteString("grantExpiresAt", config.Enrollment.GrantExpiresAt);
            writer.WriteNumber("maxUses", config.Enrollment.MaxUses);
            WriteScopeArray(writer, "maxScopes", config.Enrollment.MaxScopes);
            writer.WriteBoolean("allowCritical", config.Enrollment.AllowCritical);
            writer.WriteEndObject();
            writer.WriteString("signatureAlgorithm", config.SignatureAlgorithm);
            writer.WriteEndObject();
        });

    public static string SerializeConnectInit(ConnectInitV2 request)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", request.Type);
            writer.WriteNumber("ver", request.Ver);
            writer.WriteString("requestId", request.RequestId);
            writer.WriteString("configId", request.ConfigId);
            writer.WriteString("appId", request.AppId);
            writer.WriteString("clientNonce", request.ClientNonce);
            WriteIntArray(writer, "supportedVersions", request.SupportedVersions);
            WriteStringArray(writer, "supportedTransports", request.SupportedTransports);
            writer.WriteEndObject();
        });

    public static string SerializeConnectOffer(ConnectOfferV2 offer)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", offer.Type);
            writer.WriteNumber("ver", offer.Ver);
            writer.WriteString("requestId", offer.RequestId);
            writer.WriteString("configId", offer.ConfigId);
            writer.WriteString("appId", offer.AppId);
            writer.WriteString("clientNonce", offer.ClientNonce);
            writer.WriteString("hostNonce", offer.HostNonce);
            writer.WriteString("hostId", offer.HostId);
            writer.WriteNumber("selectedVersion", offer.SelectedVersion);
            writer.WriteString("selectedTransport", offer.SelectedTransport);
            writer.WriteNumber("webSocketPort", offer.WebSocketPort);
            writer.WriteString("webSocketPath", offer.WebSocketPath);
            writer.WriteString("tlsSpkiSha256", offer.TlsSpkiSha256);
            writer.WriteString("expiresAt", offer.ExpiresAt);
            writer.WriteString("signatureAlgorithm", offer.SignatureAlgorithm);
            writer.WriteEndObject();
        });

    public static string SerializeConnectOfferTranscript(ConnectInitV2 request, ConnectOfferV2 offer)
        => $"ANSIGHT-CONNECT-OFFER-V2\n{SerializeConnectInit(request)}\n{SerializeConnectOffer(offer)}";

    public static string SerializeChallenge(AuthChallengeV2 challenge)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", challenge.Type);
            writer.WriteNumber("ver", challenge.Ver);
            writer.WriteString("authSessionId", challenge.AuthSessionId);
            writer.WriteString("requestId", challenge.RequestId);
            writer.WriteString("configId", challenge.ConfigId);
            writer.WriteString("appId", challenge.AppId);
            writer.WriteString("clientNonce", challenge.ClientNonce);
            writer.WriteString("hostNonce", challenge.HostNonce);
            writer.WriteString("serverChallenge", challenge.ServerChallenge);
            writer.WriteString("expiresAt", challenge.ExpiresAt);
            writer.WriteEndObject();
        });

    public static string SerializeEnrollmentProof(PairingV2EnrollmentProofInput input)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("context", "ANSIGHT-AUTH-ENROLL-V2");
            writer.WriteString("configSignatureSha256", input.ConfigSignatureSha256);
            writer.WriteString("requestId", input.RequestId);
            writer.WriteString("clientNonce", input.ClientNonce);
            writer.WriteString("hostNonce", input.HostNonce);
            writer.WriteString("tlsSpkiSha256", input.TlsSpkiSha256);
            writer.WriteString("authSessionId", input.AuthSessionId);
            writer.WriteString("serverChallenge", input.ServerChallenge);
            writer.WriteString("ticketId", input.TicketId);
            writer.WriteString("clientKeyId", input.ClientKeyId);
            writer.WriteString("clientPublicKey", input.ClientPublicKey);
            WriteScopeArray(writer, "requestedScopes", input.RequestedScopes);
            writer.WriteBoolean("requestCritical", input.RequestCritical);
            writer.WriteEndObject();
        });

    public static string SerializeReconnectProof(PairingV2ReconnectProofInput input)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("context", "ANSIGHT-AUTH-PROVE-V2");
            writer.WriteString("requestId", input.RequestId);
            writer.WriteString("clientNonce", input.ClientNonce);
            writer.WriteString("hostNonce", input.HostNonce);
            writer.WriteString("tlsSpkiSha256", input.TlsSpkiSha256);
            writer.WriteString("authSessionId", input.AuthSessionId);
            writer.WriteString("serverChallenge", input.ServerChallenge);
            writer.WriteString("grantId", input.GrantId);
            writer.WriteString("clientKeyId", input.ClientKeyId);
            writer.WriteEndObject();
        });

    public static string SerializeGrant(PairingGrantV2 grant)
        => WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("grantId", grant.GrantId);
            writer.WriteString("hostId", grant.HostId);
            writer.WriteString("configId", grant.ConfigId);
            writer.WriteString("appId", grant.AppId);
            writer.WriteString("clientKeyId", grant.ClientKeyId);
            WriteScopeArray(writer, "allowedScopes", grant.AllowedScopes);
            writer.WriteBoolean("allowCritical", grant.AllowCritical);
            writer.WriteString("issuedAt", grant.IssuedAt);
            writer.WriteString("expiresAt", grant.ExpiresAt);
            writer.WriteString("signatureAlgorithm", grant.SignatureAlgorithm);
            writer.WriteEndObject();
        });

    public static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        var requested = new HashSet<string>(scopes ?? [], StringComparer.Ordinal);
        return scopeOrder.Where(requested.Contains).ToArray();
    }

    private static string WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    private static void WriteIntArray(Utf8JsonWriter writer, string name, IEnumerable<int> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteNumberValue(value);
        }
        writer.WriteEndArray();
    }

    private static void WriteScopeArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
        => WriteStringArray(writer, name, NormalizeScopes(values));
}

internal sealed record PairingV2EnrollmentProofInput(
    string ConfigSignatureSha256,
    string RequestId,
    string ClientNonce,
    string HostNonce,
    string TlsSpkiSha256,
    string AuthSessionId,
    string ServerChallenge,
    string TicketId,
    string ClientKeyId,
    string ClientPublicKey,
    string[] RequestedScopes,
    bool RequestCritical);

internal sealed record PairingV2ReconnectProofInput(
    string RequestId,
    string ClientNonce,
    string HostNonce,
    string TlsSpkiSha256,
    string AuthSessionId,
    string ServerChallenge,
    string GrantId,
    string ClientKeyId);
