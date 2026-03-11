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

    public static string SerializeDiscoverResponseForSignature(DiscoverResponse response)
    {
        var signable = new
        {
            type = response.Type,
            ver = response.Ver,
            hostId = response.HostId,
            hostName = response.HostName,
            wsPort = response.WsPort,
            wsPath = response.WsPath,
            hostPubKey = response.HostPubKey,
            respNonce = response.RespNonce
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }

    public static string SerializeDiscoverResponseForSignatureWithoutHostIdentity(DiscoverResponse response)
    {
        var signable = new
        {
            type = response.Type,
            ver = response.Ver,
            wsPort = response.WsPort,
            wsPath = response.WsPath,
            hostPubKey = response.HostPubKey,
            respNonce = response.RespNonce
        };

        return JsonSerializer.Serialize(signable, PairingJson.Compact);
    }
}
