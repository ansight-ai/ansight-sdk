using System.Net.WebSockets;
using System.Text.Json;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;

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
            return GetMessageType(message) == "CLIENT_APP_STATE" ? "ACK" : null;
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

        Assert.Equal(2, server.TextMessages.Count(message => GetMessageType(message) == "CLIENT_APP_STATE"));
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

    private static string? GetMessageType(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;
    }

    private static string? GetState(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("state", out var stateElement)
            ? stateElement.GetString()
            : null;
    }
}
