namespace Ansight.Pairing;

internal sealed class PairingV2Validator
{
    private readonly PairingV2ValidationPolicy policy;

    public PairingV2Validator(PairingV2ValidationPolicy? policy = null)
    {
        this.policy = policy ?? PairingV2ValidationPolicy.Default;
    }

    public bool TryValidateConfig(
        PairingConfigV2 config,
        string? expectedAppId,
        DateTimeOffset now,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.Equals(config.Schema, PairingConfigV2.SchemaName, StringComparison.Ordinal))
        {
            error = $"Unsupported secure pairing schema '{config.Schema}'.";
            return false;
        }

        if (!string.Equals(config.SignatureAlgorithm, PairingV2Crypto.SignatureAlgorithm, StringComparison.Ordinal))
        {
            error = "Secure pairing config uses an unsupported signature algorithm.";
            return false;
        }

        if (config.MinProtocolVersion < 2)
        {
            error = "Secure pairing config permits a protocol downgrade.";
            return false;
        }

        if (config.MinProtocolVersion > 2)
        {
            error = $"Secure pairing config requires unsupported protocol version {config.MinProtocolVersion}.";
            return false;
        }

        if (config.AllowedTransports.Length == 0 ||
            config.AllowedTransports.Any(transport => !string.Equals(transport, "wss", StringComparison.Ordinal)))
        {
            error = "Secure pairing config must allow only WSS transport.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedAppId) &&
            !string.Equals(config.AppId, expectedAppId.Trim(), StringComparison.Ordinal))
        {
            error = $"Pairing config appId '{config.AppId}' does not match expected app id '{expectedAppId.Trim()}'.";
            return false;
        }

        if (!TryValidateConfigTimes(config, now, out error) ||
            !TryValidateHost(config, now, out error) ||
            !TryValidateEnrollment(config, now, out error))
        {
            return false;
        }

        if (!PairingV2Crypto.Verify(
                config.Host.HostPubKey,
                config.Signature,
                PairingV2CanonicalJson.SerializeConfig(config)))
        {
            error = "Secure pairing config signature is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateOffer(
        PairingConfigV2 config,
        ConnectInitV2 request,
        ConnectOfferV2 offer,
        DateTimeOffset now,
        out string error)
    {
        if (!string.Equals(offer.Type, ConnectOfferV2.MessageType, StringComparison.Ordinal) || offer.Ver != 2 ||
            !string.Equals(offer.RequestId, request.RequestId, StringComparison.Ordinal) ||
            !string.Equals(offer.ConfigId, request.ConfigId, StringComparison.Ordinal) ||
            !string.Equals(offer.AppId, request.AppId, StringComparison.Ordinal) ||
            !PairingV2Crypto.FixedTimeEqualsBase64Url(offer.ClientNonce, request.ClientNonce))
        {
            error = "Secure connect offer does not match the bootstrap request.";
            return false;
        }

        if (!PairingV2Crypto.HasDecodedLength(request.RequestId, PairingV2Crypto.RequestIdByteCount) ||
            !PairingV2Crypto.HasDecodedLength(request.ClientNonce, PairingV2Crypto.NonceByteCount) ||
            !PairingV2Crypto.HasDecodedLength(offer.HostNonce, PairingV2Crypto.NonceByteCount))
        {
            error = "Secure connect offer contains an invalid nonce or request id.";
            return false;
        }

        if (!string.Equals(offer.HostId, config.Host.HostId, StringComparison.Ordinal) ||
            offer.SelectedVersion != 2 ||
            !string.Equals(offer.SelectedTransport, "wss", StringComparison.Ordinal) ||
            offer.WebSocketPort is < 1 or > 65535 ||
            !IsSafeWebSocketPath(offer.WebSocketPath) ||
            !string.Equals(offer.SignatureAlgorithm, PairingV2Crypto.SignatureAlgorithm, StringComparison.Ordinal))
        {
            error = "Secure connect offer selected an unauthorized endpoint or protocol.";
            return false;
        }

        if (!TryParseWindow(offer.ExpiresAt, out _, out var expiresAt) ||
            expiresAt <= now - policy.ClockSkew ||
            expiresAt > now + policy.MaximumOfferLifetime + policy.ClockSkew)
        {
            error = "Secure connect offer is expired or has an invalid lifetime.";
            return false;
        }

        var matchingPin = config.Host.TlsPins.Any(pin =>
            string.Equals(pin.TlsSpkiSha256, offer.TlsSpkiSha256, StringComparison.Ordinal) &&
            IsCurrentPin(pin, now));
        if (!matchingPin || !PairingV2Crypto.HasDecodedLength(offer.TlsSpkiSha256, 32))
        {
            error = "Secure connect offer contains an unauthorized TLS pin.";
            return false;
        }

        if (!PairingV2Crypto.Verify(
                config.Host.HostPubKey,
                offer.Signature,
                PairingV2CanonicalJson.SerializeConnectOfferTranscript(request, offer)))
        {
            error = "Secure connect offer signature is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateReconnectConfig(
        PairingConfigV2 config,
        string expectedAppId,
        DateTimeOffset now,
        out string error)
    {
        if (!string.Equals(config.Schema, PairingConfigV2.SchemaName, StringComparison.Ordinal) ||
            !string.Equals(config.AppId, expectedAppId, StringComparison.Ordinal) ||
            !string.Equals(config.SignatureAlgorithm, PairingV2Crypto.SignatureAlgorithm, StringComparison.Ordinal) ||
            config.MinProtocolVersion != 2 ||
            config.AllowedTransports.Length == 0 ||
            config.AllowedTransports.Any(transport => !string.Equals(transport, "wss", StringComparison.Ordinal)))
        {
            error = "Remembered secure pairing profile is invalid or permits downgrade.";
            return false;
        }

        return TryValidateHost(config, now, out error);
    }

    public bool TryValidateChallenge(
        PairingConfigV2 config,
        ConnectInitV2 request,
        ConnectOfferV2 offer,
        AuthChallengeV2 challenge,
        DateTimeOffset now,
        out string error)
    {
        if (!string.Equals(challenge.Type, AuthChallengeV2.MessageType, StringComparison.Ordinal) || challenge.Ver != 2 ||
            !string.Equals(challenge.RequestId, request.RequestId, StringComparison.Ordinal) ||
            !string.Equals(challenge.ConfigId, config.ConfigId, StringComparison.Ordinal) ||
            !string.Equals(challenge.AppId, config.AppId, StringComparison.Ordinal) ||
            !PairingV2Crypto.FixedTimeEqualsBase64Url(challenge.ClientNonce, request.ClientNonce) ||
            !PairingV2Crypto.FixedTimeEqualsBase64Url(challenge.HostNonce, offer.HostNonce) ||
            !PairingV2Crypto.HasDecodedLength(challenge.AuthSessionId, PairingV2Crypto.RequestIdByteCount) ||
            !PairingV2Crypto.HasDecodedLength(challenge.ServerChallenge, PairingV2Crypto.NonceByteCount))
        {
            error = "Secure authentication challenge does not match the signed offer.";
            return false;
        }

        if (!TryParseWindow(challenge.ExpiresAt, out _, out var expiresAt) ||
            expiresAt <= now - policy.ClockSkew ||
            expiresAt > now + policy.MaximumChallengeLifetime + policy.ClockSkew)
        {
            error = "Secure authentication challenge is expired or has an invalid lifetime.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryValidateGrant(
        PairingConfigV2 config,
        string clientKeyId,
        PairingGrantV2 grant,
        DateTimeOffset now,
        out string error)
    {
        if (!string.Equals(grant.SignatureAlgorithm, PairingV2Crypto.SignatureAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(grant.HostId, config.Host.HostId, StringComparison.Ordinal) ||
            !string.Equals(grant.ConfigId, config.ConfigId, StringComparison.Ordinal) ||
            !string.Equals(grant.AppId, config.AppId, StringComparison.Ordinal) ||
            !string.Equals(grant.ClientKeyId, clientKeyId, StringComparison.Ordinal))
        {
            error = "Secure pairing grant is not bound to this host, app, config, and client key.";
            return false;
        }

        var normalizedScopes = PairingV2CanonicalJson.NormalizeScopes(grant.AllowedScopes);
        if (!normalizedScopes.SequenceEqual(grant.AllowedScopes, StringComparer.Ordinal) ||
            normalizedScopes.Except(config.Enrollment.MaxScopes, StringComparer.Ordinal).Any() ||
            (grant.AllowCritical && !config.Enrollment.AllowCritical))
        {
            error = "Secure pairing grant contains unauthorized scopes.";
            return false;
        }

        if (!TryParseWindow(grant.IssuedAt, out var issuedAt, out _) ||
            !TryParseWindow(grant.ExpiresAt, out _, out var expiresAt) ||
            !PairingV2Crypto.TryParseTimestamp(config.Enrollment.GrantExpiresAt, out var maximumGrantExpiry) ||
            issuedAt > now + policy.ClockSkew || expiresAt <= now - policy.ClockSkew ||
            expiresAt - issuedAt > policy.MaximumGrantLifetime ||
            expiresAt > maximumGrantExpiry)
        {
            error = "Secure pairing grant is expired or has an invalid lifetime.";
            return false;
        }

        if (!PairingV2Crypto.Verify(config.Host.HostPubKey, grant.Signature, PairingV2CanonicalJson.SerializeGrant(grant)))
        {
            error = "Secure pairing grant signature is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateConfigTimes(PairingConfigV2 config, DateTimeOffset now, out string error)
    {
        if (!TryParseWindow(config.IssuedAt, out var issuedAt, out _) ||
            !TryParseWindow(config.ExpiresAt, out _, out var expiresAt) ||
            issuedAt > now + policy.ClockSkew || expiresAt <= now - policy.ClockSkew ||
            expiresAt <= issuedAt || expiresAt - issuedAt > policy.MaximumConfigLifetime)
        {
            error = "Secure pairing config is expired or has an invalid lifetime.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateHost(PairingConfigV2 config, DateTimeOffset now, out string error)
    {
        string fingerprint;
        try
        {
            fingerprint = PairingV2Crypto.ComputeSpkiFingerprint(config.Host.HostPubKey);
        }
        catch (Exception exception) when (exception is FormatException or System.Security.Cryptography.CryptographicException)
        {
            error = "Secure pairing config host public key is invalid.";
            return false;
        }

        if (!PairingV2Crypto.FixedTimeEqualsBase64Url(config.Host.HostPubKeyFingerprint, fingerprint) ||
            !PairingV2Crypto.FixedTimeEqualsBase64Url(config.Host.HostId, fingerprint))
        {
            error = "Secure pairing config host key fingerprint is invalid.";
            return false;
        }

        if (config.Host.DiscoveryPort is < 1 or > 65535 ||
            config.Host.TlsPins.Length == 0 ||
            config.Host.TlsPins.Any(pin => !PairingV2Crypto.HasDecodedLength(pin.TlsSpkiSha256, 32)) ||
            !config.Host.TlsPins.Any(pin => IsCurrentPin(pin, now)))
        {
            error = "Secure pairing config does not contain a currently valid TLS pin.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryValidateEnrollment(PairingConfigV2 config, DateTimeOffset now, out string error)
    {
        var enrollment = config.Enrollment;
        var normalizedScopes = PairingV2CanonicalJson.NormalizeScopes(enrollment.MaxScopes);
        if (enrollment.MaxUses != 1 || !PairingV2Crypto.HasDecodedLength(enrollment.Secret, 32) ||
            !normalizedScopes.SequenceEqual(enrollment.MaxScopes, StringComparer.Ordinal))
        {
            error = "Secure pairing enrollment material is invalid.";
            return false;
        }

        if (!TryParseWindow(enrollment.ExpiresAt, out _, out var enrollmentExpiresAt) ||
            !TryParseWindow(enrollment.GrantExpiresAt, out _, out var grantExpiresAt) ||
            !PairingV2Crypto.TryParseTimestamp(config.ExpiresAt, out var configExpiresAt) ||
            enrollmentExpiresAt <= now - policy.ClockSkew ||
            enrollmentExpiresAt > configExpiresAt ||
            grantExpiresAt <= now - policy.ClockSkew ||
            grantExpiresAt - now > policy.MaximumGrantLifetime + policy.ClockSkew)
        {
            error = "Secure pairing enrollment material is expired or has an invalid lifetime.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IsCurrentPin(PairingTlsPin pin, DateTimeOffset now)
    {
        return PairingV2Crypto.TryParseTimestamp(pin.NotBefore, out var notBefore) &&
               PairingV2Crypto.TryParseTimestamp(pin.NotAfter, out var notAfter) &&
               notBefore <= now && now < notAfter;
    }

    private static bool IsSafeWebSocketPath(string path)
        => path.StartsWith("/ws/v2/", StringComparison.Ordinal) &&
           !path.Contains('?', StringComparison.Ordinal) &&
           !path.Contains('#', StringComparison.Ordinal) &&
           Uri.TryCreate(path, UriKind.Relative, out _);

    private static bool TryParseWindow(string value, out DateTimeOffset first, out DateTimeOffset second)
    {
        var success = PairingV2Crypto.TryParseTimestamp(value, out var parsed);
        first = parsed;
        second = parsed;
        return success;
    }

}
