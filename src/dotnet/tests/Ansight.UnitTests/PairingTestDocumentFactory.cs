using System.Security.Cryptography;
using System.Text;
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
        string? hostId = "host-1",
        string? hostName = "test-host",
        int discoveryPort = 41000,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var issued = issuedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        var expires = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10);
        var publicKey = Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo());

        var config = new PairingConfig
        {
            Schema = PairingConfig.SchemaName,
            ConfigId = configId,
            AppId = appId,
            AppName = appName,
            IssuedAt = issued,
            ExpiresAt = expires,
            OneTimeToken = oneTimeToken,
            Host = new PairingHost
            {
                HostId = hostId,
                HostName = hostName,
                DiscoveryPort = discoveryPort,
                HostPubKey = publicKey,
                HostPubKeyFingerprint = "fingerprint-1"
            },
            Challenge = new PairingChallenge
            {
                Alg = "ECDH-P256",
                ChallengePubKey = challengePubKey,
                RequireProofOnFirstPair = true
            },
            Signature = string.Empty
        };

        var signable = PairingCanonicalJson.SerializePairingConfigForSignature(config);
        var signature = signingKey.SignData(Encoding.UTF8.GetBytes(signable), HashAlgorithmName.SHA256);
        config.Signature = Convert.ToBase64String(signature);
        return config;
    }

    public static PairingConfigDocument CreateConfigDocument(
        PairingConfig? config = null,
        PairingDiscoveryHint? discoveryHint = null)
    {
        if (config is null)
        {
            using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            config = CreateSignedConfig(signingKey);
        }

        return new PairingConfigDocument
        {
            Schema = PairingConfigDocument.SchemaName,
            Config = config,
            Discovery = discoveryHint ?? CreateDiscoveryHint()
        };
    }

    public static PairingDiscoveryHint CreateDiscoveryHint(
        string hostAddress = "127.0.0.1",
        string[]? hostAddresses = null,
        int? discoveryPort = null,
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
            DiscoveryPort = discoveryPort,
            HostName = hostName,
            WifiName = wifiName,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow
        };
    }

    public static string CreateConfigDocumentJson(
        PairingConfig? config = null,
        PairingDiscoveryHint? discoveryHint = null)
    {
        return PairingConfigDocumentJson.Serialize(CreateConfigDocument(config, discoveryHint));
    }

    public static string CreateCompactConfigDocument(
        PairingConfig? config = null,
        PairingDiscoveryHint? discoveryHint = null)
    {
        return PairingConfigCodeGenerator.Serialize(CreateConfigDocument(config, discoveryHint));
    }
}
