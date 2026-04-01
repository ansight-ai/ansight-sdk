using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingSessionConnectorTests
{
    [Fact]
    public async Task SendConnectRequestAsync_IncludesStableProcessSessionId()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;

        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey);
        config.Host.DiscoveryPort = listenerEndPoint.Port;

        var method = typeof(PairingSessionConnector).GetMethod(
            "SendConnectRequestAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var responseTask = (Task<ConnectResponse?>)method!.Invoke(
            obj: null,
            parameters:
            [
                config,
                "Unit Test App",
                IPAddress.Loopback,
                CancellationToken.None
            ])!;

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.False(string.IsNullOrWhiteSpace(parsedRequest!.ProcessSessionId));
        Assert.Equal(ProcessSessionIdentity.Current, parsedRequest.ProcessSessionId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "CONNECT_RESP",
                Ver = 1,
                Accepted = true,
                Reason = "Ok",
                HostId = "host-1",
                HostName = "Studio",
                Message = "Accepted"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var response = await responseTask;
        Assert.NotNull(response);
        Assert.True(response!.Accepted);
    }
}
