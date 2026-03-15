using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.Discovery.Multicast;

public sealed class MulticastPairingHostDiscoveryStrategy : IPairingHostDiscoveryStrategy
{
    public static MulticastPairingHostDiscoveryStrategy Instance { get; } = new();

    public async Task<IPAddress?> DiscoverHostAsync(PairingConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Trust.AllowLanDiscovery)
        {
            return null;
        }

        var discoveredHost = await MulticastDiscoveryClient.DiscoverHostAsync(
            config.AppId,
            response =>
                string.Equals(response.HostPubKey, config.Host.HostPubKey, StringComparison.Ordinal) &&
                VerifyDiscoverResponseSignature(response, config.Host.HostPubKey),
            cancellationToken);

        return discoveredHost?.Address;
    }

    private static bool VerifyDiscoverResponseSignature(DiscoverResponse response, string hostPubKeyBase64)
    {
        try
        {
            var publicKey = Convert.FromBase64String(hostPubKeyBase64);
            var signature = Convert.FromBase64String(response.Sig);

            using var hostKey = ECDsa.Create();
            hostKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            var signables = new[]
            {
                SerializeDiscoverResponseForSignature(response),
                SerializeDiscoverResponseForSignatureWithoutHostIdentity(response)
            };

            foreach (var signable in signables)
            {
                if (hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string SerializeDiscoverResponseForSignature(DiscoverResponse response)
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

        return JsonSerializer.Serialize(signable, MulticastJson.Compact);
    }

    private static string SerializeDiscoverResponseForSignatureWithoutHostIdentity(DiscoverResponse response)
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

        return JsonSerializer.Serialize(signable, MulticastJson.Compact);
    }
}
