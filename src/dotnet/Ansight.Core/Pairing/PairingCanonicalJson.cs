using System.Text.Json;

namespace Ansight.Pairing;

internal static class PairingCanonicalJson
{
    public static string SerializePairingConfigForSignature(PairingConfig config)
    {
        var signable = new
        {
            schema = config.Schema,
            configId = config.ConfigId,
            appId = config.AppId,
            appName = config.AppName,
            issuedAt = config.IssuedAt,
            expiresAt = config.ExpiresAt,
            oneTimeToken = config.OneTimeToken,
            host = new
            {
                hostPubKey = config.Host.HostPubKey,
                hostPubKeyFingerprint = config.Host.HostPubKeyFingerprint
            },
            challenge = new
            {
                alg = config.Challenge.Alg,
                challengePubKey = config.Challenge.ChallengePubKey,
                requireProofOnFirstPair = config.Challenge.RequireProofOnFirstPair
            }
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }

    public static string SerializePairingConfigWithLegacyTrustForSignature(PairingConfig config)
    {
        var signable = new
        {
            schema = config.Schema,
            configId = config.ConfigId,
            appId = config.AppId,
            appName = config.AppName,
            issuedAt = config.IssuedAt,
            expiresAt = config.ExpiresAt,
            oneTimeToken = config.OneTimeToken,
            host = new
            {
                hostPubKey = config.Host.HostPubKey,
                hostPubKeyFingerprint = config.Host.HostPubKeyFingerprint
            },
            challenge = new
            {
                alg = config.Challenge.Alg,
                challengePubKey = config.Challenge.ChallengePubKey,
                requireProofOnFirstPair = config.Challenge.RequireProofOnFirstPair
            },
            trust = new
            {
                mode = "pinned-key+token+challenge",
                requireTokenOnFirstPair = true,
                allowLanDiscovery = false
            }
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }
}
