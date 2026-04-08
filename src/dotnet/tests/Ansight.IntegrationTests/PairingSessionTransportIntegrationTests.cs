using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.IntegrationTests;

public sealed class PairingSessionTransportIntegrationTests
{
    [Fact]
    public async Task SendControlRequestAsync_SendsEnvelopeAndWaitsForResponse()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync(message =>
        {
            var request = JsonSerializer.Deserialize<PairingControlEnvelope>(message, PairingJson.Compact);
            if (request is null)
            {
                return null;
            }

            return JsonSerializer.Serialize(
                new PairingControlEnvelope
                {
                    Type = PairingControlEnvelope.ResponseType,
                    Id = "host.response",
                    ReplyTo = request.Id,
                    Action = request.Action,
                    Success = true,
                    Message = "ok"
                },
                PairingJson.Compact);
        });
        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var result = await transport.SendControlRequestAsync(
            "test.ping",
            new JsonObject
            {
                ["value"] = 42
            },
            outboundProgressMessage: "ping",
            successMessage: "ok",
            failurePrefix: "failed",
            progress: null,
            acknowledgementTimeout: TimeSpan.FromSeconds(2),
            cancellationToken: CancellationToken.None);

        await server.WaitForTextMessagesAsync(1, TimeSpan.FromSeconds(2));

        Assert.True(result.Success, result.Message);
        Assert.Single(server.TextMessages);
        Assert.Contains(PairingControlEnvelope.RequestType, server.TextMessages[0], StringComparison.Ordinal);
        Assert.Contains("\"action\":\"test.ping\"", server.TextMessages[0], StringComparison.Ordinal);

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

    [Fact]
    public async Task SendBinaryAsync_WithFragments_TransfersBinaryPayload()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync();
        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var payload = Encoding.UTF8.GetBytes("frame-payload");
        var result = await transport.SendBinaryAsync(
            async (sendFragmentAsync, cancellationToken) =>
            {
                await sendFragmentAsync(payload.AsMemory(0, 5), endOfMessage: false, cancellationToken);
                await sendFragmentAsync(payload.AsMemory(5, 1), endOfMessage: false, cancellationToken);
                await sendFragmentAsync(payload.AsMemory(6), endOfMessage: true, cancellationToken);
            },
            CancellationToken.None);

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
