using System.Net.WebSockets;
using System.Text;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;

namespace Ansight.IntegrationTests;

public sealed class PairingSessionTransportIntegrationTests
{
    [Fact]
    public async Task SendRequestAsync_SendsPayloadAndWaitsForAck()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync(_ => "ACK");
        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var result = await transport.SendRequestAsync(
            "{\"type\":\"PING\"}",
            outboundProgressMessage: null,
            successMessage: "ok",
            failurePrefix: "failed",
            progress: null,
            acknowledgementTimeout: TimeSpan.FromSeconds(2),
            cancellationToken: CancellationToken.None);

        await server.WaitForTextMessagesAsync(1, TimeSpan.FromSeconds(2));

        Assert.True(result.Success, result.Message);
        Assert.Single(server.TextMessages);
        Assert.Contains("\"type\":\"PING\"", server.TextMessages[0], StringComparison.Ordinal);

        await transport.CloseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SendBinaryAsync_TransfersBinaryPayload()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync();
        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var payload = Encoding.UTF8.GetBytes("frame-payload");
        var result = await transport.SendBinaryAsync(payload, WebSocketMessageType.Binary, CancellationToken.None);

        await server.WaitForBinaryMessagesAsync(1, TimeSpan.FromSeconds(2));

        Assert.True(result.Success, result.Message);
        Assert.Single(server.BinaryMessages);
        Assert.Equal(payload, server.BinaryMessages[0]);

        await transport.CloseAsync(CancellationToken.None);
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri webSocketUri)
    {
        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(webSocketUri, CancellationToken.None);
        return webSocket;
    }
}
