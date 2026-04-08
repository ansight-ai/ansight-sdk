using System.Net.WebSockets;
using System.Text.Json;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.IntegrationTests;

[Collection("RuntimeLifecycle")]
public sealed class PairingSessionAppStateStreamerIntegrationTests
{
    [Fact]
    public async Task StartAsync_SendsSeededAppStateAndSubsequentDistinctTransitions()
    {
        Runtime.SetAppLifecycleState(AppLifecycleState.Unknown, DateTimeOffset.Parse("2026-03-22T00:59:00Z"));
        Runtime.SetAppLifecycleState(AppLifecycleState.Foreground, DateTimeOffset.Parse("2026-03-22T01:00:00Z"));

        await using var server = await LoopbackWebSocketServer.StartAsync(message =>
        {
            var request = TryParseEnvelope(message);
            if (request is null || !string.Equals(request.Type, PairingControlEnvelope.RequestType, StringComparison.Ordinal))
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
        using var streamer = new PairingSessionAppStateStreamer(transport);

        var startResult = await streamer.StartAsync(progress: null, CancellationToken.None);
        await server.WaitForTextMessagesAsync(1, TimeSpan.FromSeconds(5));

        Assert.True(startResult.Success, startResult.Message);
        Assert.Equal("foreground", GetState(server.TextMessages[0]));

        Runtime.SetAppLifecycleState(AppLifecycleState.Background, DateTimeOffset.Parse("2026-03-22T01:01:00Z"));
        await server.WaitForTextMessagesAsync(2, TimeSpan.FromSeconds(5));
        Runtime.SetAppLifecycleState(AppLifecycleState.Background, DateTimeOffset.Parse("2026-03-22T01:02:00Z"));
        await Task.Delay(250);

        Assert.Equal(2, server.TextMessages.Count(message => GetMessageAction(message) == PairingControlActions.AppState));
        Assert.Equal("background", GetState(server.TextMessages[1]));

        await streamer.StopAsync(CancellationToken.None);
        await transport.CloseAsync(CancellationToken.None);
        Runtime.SetAppLifecycleState(AppLifecycleState.Unknown, DateTimeOffset.Parse("2026-03-22T01:03:00Z"));
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri webSocketUri)
    {
        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(webSocketUri, CancellationToken.None);
        return webSocket;
    }

    private static PairingControlEnvelope? TryParseEnvelope(string json)
    {
        return JsonSerializer.Deserialize<PairingControlEnvelope>(json, PairingJson.Compact);
    }

    private static string? GetMessageAction(string json)
    {
        return TryParseEnvelope(json)?.Action;
    }

    private static string? GetState(string json)
    {
        var payload = TryParseEnvelope(json)?.Payload;
        return payload?["state"]?.GetValue<string>();
    }
}
