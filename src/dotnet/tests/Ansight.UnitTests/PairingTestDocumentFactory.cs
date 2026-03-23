using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.UnitTests;

internal static class PairingTestDocumentFactory
{
    public static PairingConfig CreateSignedConfig(
        ECDsa signingKey,
        string configId = "cfg-1",
        string appId = "com.ansight.test",
        string appName = "Ansight Test",
        string oneTimeToken = "token-1",
        string challengePubKey = "challenge-key",
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var issued = issuedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        var expires = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10);
        var publicKey = Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo());

        var config = new PairingConfig
        {
            Schema = "ansight.pairing-config.v1",
            ConfigId = configId,
            AppId = appId,
            AppName = appName,
            IssuedAt = issued,
            ExpiresAt = expires,
            OneTimeToken = oneTimeToken,
            Host = new PairingHost
            {
                HostId = "host-1",
                HostName = "test-host",
                DiscoveryPort = 41000,
                HostPubKey = publicKey,
                HostPubKeyFingerprint = "fingerprint-1"
            },
            Challenge = new PairingChallenge
            {
                Alg = "ECDH-P256",
                ChallengePubKey = challengePubKey,
                RequireProofOnFirstPair = true
            },
            Trust = new PairingTrust
            {
                Mode = "developer",
                RequireTokenOnFirstPair = true,
                AllowLanDiscovery = true
            },
            Signature = string.Empty
        };

        var signable = PairingCanonicalJson.SerializePairingConfigForSignature(config);
        var signature = signingKey.SignData(Encoding.UTF8.GetBytes(signable), HashAlgorithmName.SHA256);
        config.Signature = Convert.ToBase64String(signature);
        return config;
    }

    public static PairingConnectionHint CreateConnectionHint(
        string configId = "cfg-override",
        string oneTimeToken = "token-override",
        string challengePubKey = "challenge-override",
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        return new PairingConnectionHint
        {
            Schema = PairingConnectionHint.SchemaName,
            ConfigId = configId,
            IssuedAt = issuedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            OneTimeToken = oneTimeToken,
            Challenge = new PairingChallenge
            {
                Alg = "ECDH-P256",
                ChallengePubKey = challengePubKey,
                RequireProofOnFirstPair = false
            }
        };
    }

    public static PairingDiscoveryHint CreateDiscoveryHint(
        string hostAddress = "127.0.0.1",
        string[]? hostAddresses = null,
        string? hostName = "test-host",
        string? wifiName = null,
        string? source = "unit-test",
        DateTimeOffset? capturedAt = null)
    {
        var resolvedHostAddresses = hostAddresses ?? new[] { hostAddress };
        return new PairingDiscoveryHint
        {
            Schema = PairingDiscoveryHint.SchemaName,
            Source = source,
            HostAddresses = resolvedHostAddresses,
            HostName = hostName,
            WifiName = wifiName,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow
        };
    }

    public static PairingQrConnectionPayload CreateQrConnectionPayload(
        PairingConnectionHint? connectionHint = null,
        PairingDiscoveryHint? discoveryHint = null)
    {
        return new PairingQrConnectionPayload
        {
            Schema = PairingQrConnectionPayload.SchemaName,
            Connection = connectionHint ?? CreateConnectionHint(),
            Discovery = discoveryHint ?? CreateDiscoveryHint()
        };
    }

    public static string CreateBootstrapJson(
        PairingConfig pairingConfig,
        PairingConnectionHint? connectionHint = null)
    {
        var bootstrap = new PairingBootstrapDocument
        {
            Schema = PairingBootstrapDocument.SchemaName,
            PairingConfig = pairingConfig,
            ConnectionHint = connectionHint,
            Discovery = CreateDiscoveryHint(hostName: null, wifiName: null, capturedAt: null)
        };

        return JsonSerializer.Serialize(bootstrap, PairingJson.Compact);
    }
}
