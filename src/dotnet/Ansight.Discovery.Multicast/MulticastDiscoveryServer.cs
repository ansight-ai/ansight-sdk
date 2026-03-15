using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.Discovery.Multicast;

public sealed class MulticastDiscoveryServer
{
    public async Task RunAsync(
        Func<IPEndPoint, DiscoverRequest, Task<DiscoverResponse?>> onDiscoverAsync,
        Func<IPEndPoint, ConnectRequest, Task<ConnectResponse?>> onConnectAsync,
        Action<bool, string>? onStatusChanged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onDiscoverAsync);
        ArgumentNullException.ThrowIfNull(onConnectAsync);

        UdpClient udpClient;
        try
        {
            udpClient = new UdpClient(AddressFamily.InterNetwork);
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastDiscoveryDefaults.DiscoveryPort));
            udpClient.JoinMulticastGroup(MulticastDiscoveryDefaults.MulticastAddress);
            udpClient.MulticastLoopback = true;
            onStatusChanged?.Invoke(true, $"Discovery listener started on port {MulticastDiscoveryDefaults.DiscoveryPort}.");
        }
        catch (SocketException ex)
        {
            onStatusChanged?.Invoke(false, $"Unable to start discovery listener: {ex.Message}");
            return;
        }

        using (udpClient)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    UdpReceiveResult receiveResult;
                    try
                    {
                        receiveResult = await udpClient.ReceiveAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    await HandleMessageAsync(udpClient, receiveResult, onDiscoverAsync, onConnectAsync);
                }
            }
            catch (SocketException ex)
            {
                onStatusChanged?.Invoke(false, $"Discovery listener error: {ex.Message}");
            }
            finally
            {
                onStatusChanged?.Invoke(false, "Discovery listener stopped.");
            }
        }
    }

    private static async Task HandleMessageAsync(
        UdpClient udpClient,
        UdpReceiveResult receiveResult,
        Func<IPEndPoint, DiscoverRequest, Task<DiscoverResponse?>> onDiscoverAsync,
        Func<IPEndPoint, ConnectRequest, Task<ConnectResponse?>> onConnectAsync)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(receiveResult.Buffer);
        }
        catch
        {
            return;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var messageType = typeElement.GetString();
            if (string.Equals(messageType, "DISCOVER_REQ", StringComparison.Ordinal))
            {
                var request = document.RootElement.Deserialize<DiscoverRequest>(MulticastJson.Compact);
                if (request is null)
                {
                    return;
                }

                var response = await onDiscoverAsync(receiveResult.RemoteEndPoint, request);
                if (response is null)
                {
                    return;
                }

                var bytes = JsonSerializer.SerializeToUtf8Bytes(response, MulticastJson.Compact);
                await udpClient.SendAsync(bytes, bytes.Length, receiveResult.RemoteEndPoint);
                return;
            }

            if (!string.Equals(messageType, "CONNECT_REQ", StringComparison.Ordinal))
            {
                return;
            }

            var connectRequest = document.RootElement.Deserialize<ConnectRequest>(MulticastJson.Compact);
            if (connectRequest is null)
            {
                return;
            }

            var connectResponse = await onConnectAsync(receiveResult.RemoteEndPoint, connectRequest);
            if (connectResponse is null)
            {
                return;
            }

            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(connectResponse, MulticastJson.Compact);
            await udpClient.SendAsync(responseBytes, responseBytes.Length, receiveResult.RemoteEndPoint);
        }
    }
}
