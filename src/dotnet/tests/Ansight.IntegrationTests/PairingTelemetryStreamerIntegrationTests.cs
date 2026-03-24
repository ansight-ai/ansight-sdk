using System.Net.WebSockets;
using System.Text.Json;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;

namespace Ansight.IntegrationTests;

public sealed class PairingTelemetryStreamerIntegrationTests
{
    [Fact]
    public async Task StartAsync_StreamsChannelsMetricsAndEvents()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync(message =>
        {
            return GetMessageType(message) == "CLIENT_EVENTS" ? "ACK" : null;
        });

        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var channel = TestDataSink.CreateChannel(42, "render");
        var metric = new Metric
        {
            Channel = channel.Id,
            Value = 123,
            CapturedAtUtc = DateTime.UtcNow.AddSeconds(-2)
        };
        var appEvent = new AppEvent("CheckoutPage", AppEventType.ScreenViewed, "route=/checkout", DateTime.UtcNow.AddSeconds(-1), externalId: null, channel.Id);
        var dataSink = new TestDataSink([channel], [metric], [appEvent]);
        using var streamer = new TelemetryStreamer(transport);

        var result = await streamer.StartAsync(dataSink, progress: null, CancellationToken.None);
        await server.WaitForTextMessagesAsync(3, TimeSpan.FromSeconds(5));

        Assert.True(result.Success, result.Message);
        Assert.Contains(server.TextMessages, message => GetMessageType(message) == "CLIENT_METRIC_CHANNELS");
        Assert.Contains(server.TextMessages, message => GetMessageType(message) == "CLIENT_METRICS");
        Assert.Contains(server.TextMessages, message => GetMessageType(message) == "CLIENT_EVENTS");
        Assert.Contains(server.TextMessages, message => message.Contains("\"eventType\":\"ScreenViewed\"", StringComparison.Ordinal));

        await streamer.StopAsync(progress: null, CancellationToken.None);
        await transport.CloseAsync(CancellationToken.None);
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
}
