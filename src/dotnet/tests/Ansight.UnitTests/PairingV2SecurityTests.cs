using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Pairing;
using Ansight.Pairing.Models;
using Ansight.Tools;

namespace Ansight.UnitTests;

public sealed class PairingV2SecurityTests
{
    [Fact]
    public void TryValidateConfig_AcceptsCanonicalSignedConfig()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var config = CreateConfig(hostKey, now);

        var validator = new PairingV2Validator();
        var valid = validator.TryValidateConfig(config, config.AppId, now, out var error);

        Assert.True(valid, error);
        Assert.Equal(PairingV2Crypto.SignatureAlgorithm, config.SignatureAlgorithm);
        Assert.DoesNotContain("\"signature\":", PairingV2CanonicalJson.SerializeConfig(config), StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateConfig_RejectsFingerprintAndTransportTampering()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var config = CreateConfig(hostKey, now);
        config.Host.HostPubKeyFingerprint = PairingCrypto.CreateBase64UrlRandom(32);
        config.AllowedTransports = ["ws"];

        var valid = new PairingV2Validator().TryValidateConfig(config, config.AppId, now, out var error);

        Assert.False(valid);
        Assert.Contains("WSS", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidateOffer_BindsRequestNoncesEndpointAndTlsPin()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var config = CreateConfig(hostKey, now);
        var request = new ConnectInitV2
        {
            RequestId = PairingCrypto.CreateBase64UrlRandom(16),
            ConfigId = config.ConfigId,
            AppId = config.AppId,
            ClientNonce = PairingCrypto.CreateBase64UrlRandom(32)
        };
        var offer = CreateOffer(config, request, hostKey, now);
        var validator = new PairingV2Validator();

        Assert.True(validator.TryValidateOffer(config, request, offer, now, out var error), error);

        offer.ClientNonce = PairingCrypto.CreateBase64UrlRandom(32);
        Assert.False(validator.TryValidateOffer(config, request, offer, now, out error));
        Assert.Contains("bootstrap request", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnrollmentProof_UsesDirectHmacSha256OverCanonicalTranscript()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var secretText = PairingCrypto.ToBase64Url(secret);
        var input = new PairingV2EnrollmentProofInput(
            "config-hash",
            "request-id",
            "client-nonce",
            "host-nonce",
            "tls-pin",
            "auth-id",
            "challenge",
            "ticket",
            "key-id",
            "public-key",
            ["Read"],
            false);
        var canonical = PairingV2CanonicalJson.SerializeEnrollmentProof(input);

        using var hmac = new HMACSHA256(secret);
        var expected = PairingCrypto.ToBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

        Assert.Equal(expected, PairingV2Crypto.ComputeEnrollmentProof(secretText, canonical));
    }

    [Fact]
    public void SecureSessionGate_RejectsWrongSessionAndDuplicateCallId()
    {
        var context = new PairingV2SessionContext(
            "session-1",
            new PairingGrantV2
            {
                GrantId = "grant",
                HostId = "host",
                ConfigId = "config",
                AppId = "app",
                ClientKeyId = "key",
                AllowedScopes = ["Read"],
                AllowCritical = false,
                IssuedAt = "2026-07-13T00:00:00.0000000Z",
                ExpiresAt = "2026-07-14T00:00:00.0000000Z",
                SignatureAlgorithm = PairingV2Crypto.SignatureAlgorithm,
                Signature = "signature"
            });
        var gate = new PairingV2SessionGate(context);
        var request = new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.CallType,
            Id = "call-1",
            SessionId = "session-1",
            Payload = new JsonObject()
        };

        Assert.True(gate.TryAccept(request, out _, out _));
        Assert.False(gate.TryAccept(request, out var code, out _));
        Assert.Equal("tool_request_replayed", code);

        var wrongSession = new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.QueryType,
            Id = "query-1",
            SessionId = "other",
            Payload = new JsonObject()
        };
        Assert.False(gate.TryAccept(wrongSession, out code, out _));
        Assert.Equal("tool_session_mismatch", code);
    }

    [Fact]
    public async Task LegacyConnector_RequiresExplicitInsecureCompatibility()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(hostKey),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(IPAddress.Loopback.ToString())
        };

        var result = await connector.ConnectAsync(document, "test", options: null, progress: null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(PairingFailureCodes.InsecureV1Disabled, result.FailureCode);
    }

    [Fact]
    public void ConfigDocumentService_ParsesAndValidatesV2WithoutDowngrade()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = CreateConfig(hostKey, DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(config, PairingJson.Compact);
        var service = new PairingConfigDocumentService();

        var parsed = service.TryParseAndValidateDocument(json, config.AppId, out var document, out var error);

        Assert.True(parsed, error);
        Assert.NotNull(document?.SecureConfig);
        Assert.Null(document?.Config);
        Assert.True(document?.IsSecureV2);
    }

    [Fact]
    public void ProtocolV2Credentials_DoNotEnterLegacyPlaintextStores()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = CreateConfig(hostKey, DateTimeOffset.UtcNow);
        var document = new ParsedPairingDocument { SecureConfig = config };
        var directory = Path.Combine(Path.GetTempPath(), $"ansight-v2-store-{Guid.NewGuid():N}");
        var savedPath = Path.Combine(directory, "saved.json");
        var cachePath = Path.Combine(directory, "cache.json");

        try
        {
            var savedStore = new StoredHostPairingConfigStore("v2", savedPath);
            var cacheStore = new StoredPairingDocumentCache("v2", cachePath);

            Assert.Throws<InvalidOperationException>(() => savedStore.Save(document));
            Assert.Throws<InvalidOperationException>(() => cacheStore.Save(document));
            Assert.False(File.Exists(savedPath));
            Assert.False(File.Exists(cachePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ManagedSigningKey_IsolatedBehindReplaceableProvider()
    {
        var provider = new ManagedPairingV2SigningKeyProvider();
        using var created = provider.Create();
        var signature = created.Sign("canonical transcript");

        using var reopened = provider.Open(created.KeyReference);

        Assert.Equal(created.KeyId, reopened.KeyId);
        Assert.Equal(created.PublicKey, reopened.PublicKey);
        Assert.True(PairingV2Crypto.Verify(reopened.PublicKey, signature, "canonical transcript"));
    }

    [Fact]
    public void CredentialStore_LoadsSignedReconnectGrantFromProtectedBackend()
    {
        using var hostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var config = CreateConfig(hostKey, now);
        using var clientKey = new ManagedPairingV2SigningKeyProvider().Create();
        var grant = CreateGrant(config, clientKey.KeyId, hostKey, now);
        config.Enrollment.Secret = string.Empty;
        var secureStore = new TestSecureStore();
        var store = new PairingV2CredentialStore(secureStore);
        store.Save(new PairingV2Credential
        {
            HostId = config.Host.HostId,
            AppId = config.AppId,
            ClientKeyId = clientKey.KeyId,
            ClientPublicKey = clientKey.PublicKey,
            ClientKeyReference = clientKey.KeyReference,
            Grant = grant,
            ReconnectConfig = config,
            LastHostAddress = IPAddress.Loopback.ToString(),
            DiscoveryPort = 45123
        });

        var loaded = store.TryLoadForApp(config.AppId, now, out var credential);

        Assert.True(loaded);
        Assert.NotNull(credential);
        Assert.Equal(grant.GrantId, credential.Grant.GrantId);
        Assert.Equal(IPAddress.Loopback.ToString(), credential.LastHostAddress);
    }

    internal static PairingConfigV2 CreateConfig(ECDsa hostKey, DateTimeOffset now)
    {
        var publicKey = Convert.ToBase64String(hostKey.ExportSubjectPublicKeyInfo());
        var fingerprint = PairingCrypto.ToBase64Url(SHA256.HashData(hostKey.ExportSubjectPublicKeyInfo()));
        var config = new PairingConfigV2
        {
            Schema = PairingConfigV2.SchemaName,
            ConfigId = "config-v2",
            AppId = "com.example.app",
            AppName = "Example App",
            IssuedAt = now.AddMinutes(-1).UtcDateTime.ToString("O"),
            ExpiresAt = now.AddMinutes(10).UtcDateTime.ToString("O"),
            MinProtocolVersion = 2,
            AllowedTransports = ["wss"],
            Host = new PairingHostV2
            {
                HostId = fingerprint,
                HostName = "Test Host",
                DiscoveryPort = 45123,
                HostPubKey = publicKey,
                HostPubKeyFingerprint = fingerprint,
                TlsPins =
                [
                    new PairingTlsPin
                    {
                        TlsSpkiSha256 = PairingCrypto.CreateBase64UrlRandom(32),
                        NotBefore = now.AddMinutes(-5).UtcDateTime.ToString("O"),
                        NotAfter = now.AddDays(1).UtcDateTime.ToString("O")
                    }
                ]
            },
            Enrollment = new PairingEnrollment
            {
                TicketId = PairingCrypto.CreateBase64UrlRandom(16),
                Secret = PairingCrypto.CreateBase64UrlRandom(32),
                ExpiresAt = now.AddMinutes(10).UtcDateTime.ToString("O"),
                GrantExpiresAt = now.AddDays(30).UtcDateTime.ToString("O"),
                MaxUses = 1,
                MaxScopes = ["Read"],
                AllowCritical = false
            },
            SignatureAlgorithm = PairingV2Crypto.SignatureAlgorithm,
            Signature = string.Empty
        };
        config.Signature = PairingV2Crypto.Sign(hostKey, PairingV2CanonicalJson.SerializeConfig(config));
        return config;
    }

    private static ConnectOfferV2 CreateOffer(
        PairingConfigV2 config,
        ConnectInitV2 request,
        ECDsa hostKey,
        DateTimeOffset now)
    {
        var offer = new ConnectOfferV2
        {
            Type = ConnectOfferV2.MessageType,
            Ver = 2,
            RequestId = request.RequestId,
            ConfigId = request.ConfigId,
            AppId = request.AppId,
            ClientNonce = request.ClientNonce,
            HostNonce = PairingCrypto.CreateBase64UrlRandom(32),
            HostId = config.Host.HostId,
            SelectedVersion = 2,
            SelectedTransport = "wss",
            WebSocketPort = 45124,
            WebSocketPath = "/ws/v2/offer",
            TlsSpkiSha256 = config.Host.TlsPins[0].TlsSpkiSha256,
            ExpiresAt = now.AddSeconds(10).UtcDateTime.ToString("O"),
            SignatureAlgorithm = PairingV2Crypto.SignatureAlgorithm,
            Signature = string.Empty
        };
        offer.Signature = PairingV2Crypto.Sign(hostKey, PairingV2CanonicalJson.SerializeConnectOfferTranscript(request, offer));
        return offer;
    }

    private static PairingGrantV2 CreateGrant(
        PairingConfigV2 config,
        string clientKeyId,
        ECDsa hostKey,
        DateTimeOffset now)
    {
        var grant = new PairingGrantV2
        {
            GrantId = PairingCrypto.CreateBase64UrlRandom(16),
            HostId = config.Host.HostId,
            ConfigId = config.ConfigId,
            AppId = config.AppId,
            ClientKeyId = clientKeyId,
            AllowedScopes = ["Read"],
            AllowCritical = false,
            IssuedAt = now.UtcDateTime.ToString("O"),
            ExpiresAt = now.AddDays(20).UtcDateTime.ToString("O"),
            SignatureAlgorithm = PairingV2Crypto.SignatureAlgorithm,
            Signature = string.Empty
        };
        grant.Signature = PairingV2Crypto.Sign(hostKey, PairingV2CanonicalJson.SerializeGrant(grant));
        return grant;
    }

    private sealed class TestSecureStore : IPairingSecureStore
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public bool TryGet(string key, out string? value) => values.TryGetValue(key, out value);

        public void Set(string key, string value) => values[key] = value;

        public void Remove(string key) => values.Remove(key);
    }
}
