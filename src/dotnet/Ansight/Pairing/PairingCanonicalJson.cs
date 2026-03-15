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
                hostId = config.Host.HostId,
                hostName = config.Host.HostName,
                wsPort = config.Host.WsPort,
                wsPath = config.Host.WsPath,
                discoveryPort = config.Host.DiscoveryPort,
                mdnsService = config.Host.MdnsService,
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
                mode = config.Trust.Mode,
                requireTokenOnFirstPair = config.Trust.RequireTokenOnFirstPair,
                allowLanDiscovery = config.Trust.AllowLanDiscovery
            }
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }

    public static string SerializePairingConfigForSignatureWithoutHostIdentity(PairingConfig config)
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
                wsPort = config.Host.WsPort,
                wsPath = config.Host.WsPath,
                discoveryPort = config.Host.DiscoveryPort,
                mdnsService = config.Host.MdnsService,
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
                mode = config.Trust.Mode,
                requireTokenOnFirstPair = config.Trust.RequireTokenOnFirstPair,
                allowLanDiscovery = config.Trust.AllowLanDiscovery
            }
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }

    public static string SerializeLegacyPairingConfigForSignature(PairingConfig config)
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
                hostId = config.Host.HostId,
                hostName = config.Host.HostName,
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
                mode = config.Trust.Mode,
                requireTokenOnFirstPair = config.Trust.RequireTokenOnFirstPair,
                allowLanDiscovery = config.Trust.AllowLanDiscovery
            }
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }

    public static string SerializeLegacyPairingConfigForSignatureWithoutHostIdentity(PairingConfig config)
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
                mode = config.Trust.Mode,
                requireTokenOnFirstPair = config.Trust.RequireTokenOnFirstPair,
                allowLanDiscovery = config.Trust.AllowLanDiscovery
            }
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }
}
