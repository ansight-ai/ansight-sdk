using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Ansight.Discovery.Multicast;

public static class MulticastDiscoveryClient
{
    public static async Task<DiscoveredHost?> DiscoverHostAsync(
        string appId,
        Func<DiscoverResponse, bool> acceptResponse,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentNullException.ThrowIfNull(acceptResponse);

        var nonce = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var request = new DiscoverRequest
        {
            Type = "DISCOVER_REQ",
            Ver = 1,
            Nonce = nonce,
            AppId = appId
        };

        using var udpClient = new UdpClient(0);
        udpClient.MulticastLoopback = true;

        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, MulticastJson.Compact);
        await udpClient.SendAsync(
            requestBytes,
            requestBytes.Length,
            new IPEndPoint(MulticastDiscoveryDefaults.MulticastAddress, MulticastDiscoveryDefaults.DiscoveryPort));

        await udpClient.SendAsync(
            requestBytes,
            requestBytes.Length,
            new IPEndPoint(IPAddress.Loopback, MulticastDiscoveryDefaults.DiscoveryPort));

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult receiveResult;
            try
            {
                receiveResult = await udpClient.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            DiscoverResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<DiscoverResponse>(receiveResult.Buffer, MulticastJson.Compact);
            }
            catch
            {
                continue;
            }

            if (response is null || !string.Equals(response.Type, "DISCOVER_RESP", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(response.RespNonce, nonce, StringComparison.Ordinal))
            {
                continue;
            }

            if (!acceptResponse(response))
            {
                continue;
            }

            return new DiscoveredHost(receiveResult.RemoteEndPoint.Address);
        }

        return null;
    }
}
