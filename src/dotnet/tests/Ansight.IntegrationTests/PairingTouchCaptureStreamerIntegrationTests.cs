using System.Net.WebSockets;
using System.Text.Json;
using Ansight.Input;
using Ansight.IntegrationTests.Support;
using Ansight.Pairing;

namespace Ansight.IntegrationTests;

public sealed class PairingTouchCaptureStreamerIntegrationTests
{
    [Fact]
    public async Task StartAsync_StreamsTouchInputSeparatelyFromTelemetry()
    {
        await using var server = await LoopbackWebSocketServer.StartAsync();

        using var webSocket = await ConnectAsync(server.WebSocketUri);
        using var transport = new PairingSessionTransport();
        transport.Attach(webSocket);

        var touchCaptureHub = new TouchCaptureHub(new TouchCaptureOptions());
        using var streamer = new PairingSessionTouchCaptureStreamer(transport);

        var result = await streamer.StartAsync(touchCaptureHub, progress: null, CancellationToken.None);
        touchCaptureHub.Record(new CapturedTouch(
            CapturedTouchAction.Down,
            pointerId: 7,
            pointerIndex: 0,
            pointerCount: 1,
            x: 25,
            y: 40,
            surfaceWidth: 100,
            surfaceHeight: 200,
            coordinateUnit: "pixels",
            surfaceScale: 2,
            DateTimeOffset.UtcNow));

        await server.WaitForTextMessagesAsync(1, TimeSpan.FromSeconds(5));

        Assert.True(result.Success, result.Message);
        var message = Assert.Single(server.TextMessages);
        Assert.Equal("CLIENT_TOUCH_INPUT", GetMessageType(message));
        Assert.DoesNotContain("CLIENT_EVENTS", message, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIENT_METRICS", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"touches\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"action\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"normalizedX\"", message, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(message))
        {
            var root = document.RootElement;
            Assert.Equal("ansight.touches.v1", root.GetProperty("schema").GetString());
            Assert.Equal("w", root.GetProperty("space").GetString());
            Assert.Equal("px", root.GetProperty("unit").GetString());
            Assert.Equal(100, root.GetProperty("surface")[0].GetDouble());
            Assert.Equal(200, root.GetProperty("surface")[1].GetDouble());
            Assert.Equal(2, root.GetProperty("surface")[2].GetDouble());
            var row = root.GetProperty("rows")[0];
            Assert.Equal(0, row[1].GetInt32());
            Assert.Equal(7, row[2].GetInt64());
            Assert.Equal(25, row[3].GetDouble());
            Assert.Equal(40, row[4].GetDouble());
        }

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
