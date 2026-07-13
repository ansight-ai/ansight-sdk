using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingV2SessionConnector
{
    private const int MaximumAuthenticationMessageBytes = 64 * 1024;
    private readonly PairingV2Validator validator;
    private readonly PairingV2CredentialStore credentialStore;
    private readonly IPairingV2SigningKeyProvider signingKeyProvider;
    private readonly Func<DateTimeOffset> nowProvider;

    public PairingV2SessionConnector(
        PairingV2Validator? validator = null,
        PairingV2CredentialStore? credentialStore = null,
        IPairingV2SigningKeyProvider? signingKeyProvider = null,
        Func<DateTimeOffset>? nowProvider = null)
    {
        this.validator = validator ?? new PairingV2Validator();
        this.credentialStore = credentialStore ?? new PairingV2CredentialStore();
        this.signingKeyProvider = signingKeyProvider ?? new ManagedPairingV2SigningKeyProvider();
        this.nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<PairingV2ConnectionResult> ConnectAsync(
        PairingConfigV2 config,
        IReadOnlyList<string> hostAddressCandidates,
        int discoveryPort,
        string[] requestedScopes,
        bool requestCritical,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        PairingV2ConnectionResult? lastFailure = null;
        foreach (var hostAddressCandidate in hostAddressCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IPAddress.TryParse(hostAddressCandidate, out var hostAddress))
            {
                lastFailure = PairingV2ConnectionResult.FromFailure(
                    $"Pairing host address '{hostAddressCandidate}' is not a valid IP address.",
                    PairingFailureCodes.HostAddressRequired);
                continue;
            }

            var request = new ConnectInitV2
            {
                RequestId = PairingCrypto.CreateBase64UrlRandom(PairingV2Crypto.RequestIdByteCount),
                ConfigId = config.ConfigId,
                AppId = config.AppId,
                ClientNonce = PairingCrypto.CreateBase64UrlRandom(PairingV2Crypto.NonceByteCount)
            };

            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Connection,
                $"Opening secure pairing session with {hostAddress}:{discoveryPort}.",
                source: HostConnectionSource.Transport);

            ConnectOfferV2? offer;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                offer = await SendConnectInitAsync(request, config, hostAddress, discoveryPort, timeout.Token);
            }
            catch (SocketException exception)
            {
                lastFailure = PairingV2ConnectionResult.FromFailure(
                    $"Secure UDP bootstrap failed for {hostAddress}: {exception.Message}",
                    PairingFailureCodes.UdpBootstrapFailed);
                continue;
            }

            if (offer is null)
            {
                lastFailure = PairingV2ConnectionResult.FromFailure(
                    $"No valid signed secure offer was received from {hostAddress}.",
                    PairingFailureCodes.UdpBootstrapTimeout);
                continue;
            }

            var webSocket = await ConnectPinnedWebSocketAsync(hostAddress, offer, cancellationToken);
            if (webSocket is null)
            {
                return PairingV2ConnectionResult.FromFailure(
                    "The pinned secure WebSocket endpoint did not become reachable.",
                    PairingFailureCodes.WebSocketEndpointUnreachable);
            }

            try
            {
                var authentication = await AuthenticateAsync(
                    webSocket,
                    config,
                    request,
                    offer,
                    requestedScopes,
                    requestCritical,
                    cancellationToken);
                if (!authentication.Success)
                {
                    webSocket.Dispose();
                    return PairingV2ConnectionResult.FromFailure(authentication.Message, authentication.FailureCode);
                }

                credentialStore.UpdateRouting(config, hostAddress.ToString(), discoveryPort);
                config.Enrollment.Secret = string.Empty;
                return PairingV2ConnectionResult.FromSuccess(
                    hostAddress,
                    offer,
                    webSocket,
                    authentication.Context!);
            }
            catch
            {
                webSocket.Dispose();
                throw;
            }
        }

        return lastFailure ?? PairingV2ConnectionResult.FromFailure(
            "A current secure host address is required.",
            PairingFailureCodes.HostAddressRequired);
    }

    private async Task<ConnectOfferV2?> SendConnectInitAsync(
        ConnectInitV2 request,
        PairingConfigV2 config,
        IPAddress hostAddress,
        int discoveryPort,
        CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(hostAddress.AddressFamily);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, PairingJson.Compact);
        await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(hostAddress, discoveryPort));

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udpClient.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (received.Buffer.Length > MaximumAuthenticationMessageBytes)
            {
                continue;
            }

            try
            {
                var offer = JsonSerializer.Deserialize<ConnectOfferV2>(received.Buffer, PairingJson.Compact);
                if (offer is not null && validator.TryValidateOffer(config, request, offer, nowProvider(), out _))
                {
                    return offer;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed or unrelated datagrams. Only a valid host-signed offer is authoritative.
            }
        }

        return null;
    }

    private static async Task<ClientWebSocket?> ConnectPinnedWebSocketAsync(
        IPAddress hostAddress,
        ConnectOfferV2 offer,
        CancellationToken cancellationToken)
    {
        var uri = new UriBuilder(Uri.UriSchemeWss, hostAddress.ToString(), offer.WebSocketPort, offer.WebSocketPath).Uri;
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var socket = new ClientWebSocket();
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                ValidatePinnedCertificate(certificate, errors, offer.TlsSpkiSha256);

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await socket.ConnectAsync(uri, timeout.Token);
                if (socket.State == WebSocketState.Open)
                {
                    return socket;
                }
            }
            catch when (attempt < 12)
            {
                // Retry the same signed, short-lived offer. Authentication never falls back to v1.
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            socket.Dispose();
            if (attempt < 12)
            {
                await Task.Delay(250, cancellationToken);
            }
        }

        return null;
    }

    private async Task<PairingV2AuthenticationResult> AuthenticateAsync(
        ClientWebSocket socket,
        PairingConfigV2 config,
        ConnectInitV2 request,
        ConnectOfferV2 offer,
        string[] requestedScopes,
        bool requestCritical,
        CancellationToken cancellationToken)
    {
        var challengeJson = await ReceiveTextAsync(socket, cancellationToken);
        if (TryParseAuthError(challengeJson, out var initialError))
        {
            return PairingV2AuthenticationResult.FromFailure(initialError!.Message, initialError.Code);
        }

        AuthChallengeV2? challenge;
        try
        {
            challenge = JsonSerializer.Deserialize<AuthChallengeV2>(challengeJson, PairingJson.Compact);
        }
        catch (JsonException)
        {
            challenge = null;
        }

        var challengeError = "Host sent an invalid authentication challenge.";
        if (challenge is null || !validator.TryValidateChallenge(config, request, offer, challenge, nowProvider(), out challengeError))
        {
            return PairingV2AuthenticationResult.FromFailure(
                challengeError,
                PairingFailureCodes.PairingProofInvalid);
        }

        PairingV2Credential? credential;
        var clientKeyId = string.Empty;
        var clientPublicKey = string.Empty;
        var clientKeyReference = string.Empty;
        var reconnectSent = false;
        if (credentialStore.TryLoad(config, nowProvider(), out credential) &&
            credential is not null &&
            validator.TryValidateGrant(config, credential.ClientKeyId, credential.Grant, nowProvider(), out _))
        {
            try
            {
                clientKeyId = credential.ClientKeyId;
                clientPublicKey = credential.ClientPublicKey;
                clientKeyReference = credential.ClientKeyReference;
                using var clientKey = signingKeyProvider.Open(clientKeyReference);
                var proofInput = new PairingV2ReconnectProofInput(
                    request.RequestId,
                    request.ClientNonce,
                    offer.HostNonce,
                    offer.TlsSpkiSha256,
                    challenge.AuthSessionId,
                    challenge.ServerChallenge,
                    credential.Grant.GrantId,
                    clientKeyId);
                var prove = new AuthProveV2
                {
                    AuthSessionId = challenge.AuthSessionId,
                    GrantId = credential.Grant.GrantId,
                    ClientKeyId = clientKeyId,
                    Signature = clientKey.Sign(PairingV2CanonicalJson.SerializeReconnectProof(proofInput))
                };
                await SendTextAsync(socket, JsonSerializer.Serialize(prove, PairingJson.Compact), cancellationToken);
                reconnectSent = true;
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException)
            {
                credentialStore.Remove(config.Host.HostId, config.AppId);
            }
        }

        if (!reconnectSent)
        {
            credentialStore.Remove(config.Host.HostId, config.AppId);
            if (!PairingV2Crypto.HasDecodedLength(config.Enrollment.Secret, 32))
            {
                return PairingV2AuthenticationResult.FromFailure(
                    "The enrollment ticket has already been consumed and no valid reconnect grant is available.",
                    PairingFailureCodes.PairingRequired);
            }

            using var clientKey = signingKeyProvider.Create();
            clientPublicKey = clientKey.PublicKey;
            clientKeyReference = clientKey.KeyReference;
            clientKeyId = clientKey.KeyId;
            var effectiveScopes = PairingV2CanonicalJson.NormalizeScopes(requestedScopes)
                .Intersect(config.Enrollment.MaxScopes, StringComparer.Ordinal)
                .ToArray();
            var effectiveCritical = requestCritical && config.Enrollment.AllowCritical;
            var proofInput = new PairingV2EnrollmentProofInput(
                PairingV2Crypto.ComputeConfigSignatureSha256(config.Signature),
                request.RequestId,
                request.ClientNonce,
                offer.HostNonce,
                offer.TlsSpkiSha256,
                challenge.AuthSessionId,
                challenge.ServerChallenge,
                config.Enrollment.TicketId,
                clientKeyId,
                clientPublicKey,
                effectiveScopes,
                effectiveCritical);
            var enroll = new AuthEnrollV2
            {
                AuthSessionId = challenge.AuthSessionId,
                TicketId = config.Enrollment.TicketId,
                ClientKeyId = clientKeyId,
                ClientPublicKey = clientPublicKey,
                RequestedScopes = effectiveScopes,
                RequestCritical = effectiveCritical,
                Proof = PairingV2Crypto.ComputeEnrollmentProof(
                    config.Enrollment.Secret,
                    PairingV2CanonicalJson.SerializeEnrollmentProof(proofInput))
            };
            await SendTextAsync(socket, JsonSerializer.Serialize(enroll, PairingJson.Compact), cancellationToken);
        }

        var resultJson = await ReceiveTextAsync(socket, cancellationToken);
        if (TryParseAuthError(resultJson, out var authError))
        {
            return PairingV2AuthenticationResult.FromFailure(authError!.Message, authError.Code);
        }

        AuthOkV2? authOk;
        try
        {
            authOk = JsonSerializer.Deserialize<AuthOkV2>(resultJson, PairingJson.Compact);
        }
        catch (JsonException)
        {
            authOk = null;
        }

        var grantError = "Host sent an invalid authentication result.";
        if (authOk is null ||
            !string.Equals(authOk.Type, AuthOkV2.MessageType, StringComparison.Ordinal) ||
            authOk.Ver != 2 ||
            string.IsNullOrWhiteSpace(authOk.SessionId) ||
            !validator.TryValidateGrant(config, clientKeyId, authOk.Grant, nowProvider(), out grantError))
        {
            return PairingV2AuthenticationResult.FromFailure(
                grantError,
                PairingFailureCodes.PairingProofInvalid);
        }

        credentialStore.Save(new PairingV2Credential
        {
            HostId = config.Host.HostId,
            AppId = config.AppId,
            ClientKeyId = clientKeyId,
            ClientPublicKey = clientPublicKey,
            ClientKeyReference = clientKeyReference,
            Grant = authOk.Grant,
            ReconnectConfig = CreateReconnectConfig(config)
        });

        return PairingV2AuthenticationResult.FromSuccess(new PairingV2SessionContext(authOk.SessionId, authOk.Grant));
    }

    private static bool ValidatePinnedCertificate(X509Certificate? certificate, SslPolicyErrors errors, string expectedPin)
    {
        if (certificate is null || errors != SslPolicyErrors.None)
        {
            return false;
        }

        try
        {
            using var leaf = new X509Certificate2(certificate);
            return PairingV2Crypto.FixedTimeEqualsBase64Url(
                PairingV2Crypto.ComputeTlsSpkiSha256(leaf),
                expectedPin);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool TryParseAuthError(string json, out AuthErrorV2? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), AuthErrorV2.MessageType, StringComparison.Ordinal))
            {
                error = JsonSerializer.Deserialize<AuthErrorV2>(json, PairingJson.Compact);
                return error is not null && error.Ver == 2;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("Expected a protocol-v2 authentication text message.");
            }

            if (stream.Length + result.Count > MaximumAuthenticationMessageBytes)
            {
                throw new InvalidDataException("Protocol-v2 authentication message exceeded the size limit.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    private static Task SendTextAsync(ClientWebSocket socket, string text, CancellationToken cancellationToken)
        => socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);

    private static PairingConfigV2 CreateReconnectConfig(PairingConfigV2 config)
    {
        return new PairingConfigV2
        {
            Schema = config.Schema,
            ConfigId = config.ConfigId,
            AppId = config.AppId,
            AppName = config.AppName,
            IssuedAt = config.IssuedAt,
            ExpiresAt = config.ExpiresAt,
            MinProtocolVersion = config.MinProtocolVersion,
            AllowedTransports = [.. config.AllowedTransports],
            Host = new PairingHostV2
            {
                HostId = config.Host.HostId,
                HostName = config.Host.HostName,
                DiscoveryPort = config.Host.DiscoveryPort,
                HostPubKey = config.Host.HostPubKey,
                HostPubKeyFingerprint = config.Host.HostPubKeyFingerprint,
                TlsPins = config.Host.TlsPins.Select(pin => new PairingTlsPin
                {
                    TlsSpkiSha256 = pin.TlsSpkiSha256,
                    NotBefore = pin.NotBefore,
                    NotAfter = pin.NotAfter
                }).ToArray()
            },
            Enrollment = new PairingEnrollment
            {
                TicketId = config.Enrollment.TicketId,
                Secret = string.Empty,
                ExpiresAt = config.Enrollment.ExpiresAt,
                GrantExpiresAt = config.Enrollment.GrantExpiresAt,
                MaxUses = config.Enrollment.MaxUses,
                MaxScopes = [.. config.Enrollment.MaxScopes],
                AllowCritical = config.Enrollment.AllowCritical
            },
            SignatureAlgorithm = config.SignatureAlgorithm,
            Signature = config.Signature
        };
    }
}

internal sealed record PairingV2SessionContext(string SessionId, PairingGrantV2 Grant);

internal sealed record PairingV2ConnectionResult(
    bool Success,
    string Message,
    IPAddress? HostAddress,
    ConnectOfferV2? Offer,
    ClientWebSocket? WebSocket,
    PairingV2SessionContext? Context,
    string? FailureCode)
{
    public static PairingV2ConnectionResult FromFailure(string message, string? failureCode)
        => new(false, message, null, null, null, null, failureCode);

    public static PairingV2ConnectionResult FromSuccess(
        IPAddress hostAddress,
        ConnectOfferV2 offer,
        ClientWebSocket webSocket,
        PairingV2SessionContext context)
        => new(true, "Secure host authentication succeeded.", hostAddress, offer, webSocket, context, null);
}

internal sealed record PairingV2AuthenticationResult(
    bool Success,
    string Message,
    PairingV2SessionContext? Context,
    string? FailureCode)
{
    public static PairingV2AuthenticationResult FromFailure(string message, string? failureCode)
        => new(false, message, null, failureCode);

    public static PairingV2AuthenticationResult FromSuccess(PairingV2SessionContext context)
        => new(true, "Secure host authentication succeeded.", context, null);
}
