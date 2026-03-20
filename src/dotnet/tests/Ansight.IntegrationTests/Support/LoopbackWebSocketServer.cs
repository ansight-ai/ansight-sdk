using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Ansight.IntegrationTests.Support;

internal sealed class LoopbackWebSocketServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly Func<string, string?> textReplyFactory;
    private readonly ConcurrentQueue<string> textMessages = new();
    private readonly ConcurrentQueue<byte[]> binaryMessages = new();

    private LoopbackWebSocketServer(WebApplication app, Uri webSocketUri, Func<string, string?> textReplyFactory)
    {
        this.app = app;
        WebSocketUri = webSocketUri;
        this.textReplyFactory = textReplyFactory;
    }

    public Uri WebSocketUri { get; }

    public IReadOnlyList<string> TextMessages => textMessages.ToArray();

    public IReadOnlyList<byte[]> BinaryMessages => binaryMessages.ToArray();

    public static async Task<LoopbackWebSocketServer> StartAsync(
        Func<string, string?>? textReplyFactory = null,
        CancellationToken cancellationToken = default)
    {
        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel().UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();
        var server = new LoopbackWebSocketServer(
            app,
            new Uri($"ws://127.0.0.1:{port}/ws"),
            textReplyFactory ?? (_ => null));

        app.UseWebSockets();
        app.Map("/ws", server.HandleAsync);

        await app.StartAsync(cancellationToken);
        return server;
    }

    public async Task WaitForTextMessagesAsync(int expectedCount, TimeSpan timeout)
    {
        await WaitForCountAsync(() => textMessages.Count >= expectedCount, timeout);
    }

    public async Task WaitForBinaryMessagesAsync(int expectedCount, TimeSpan timeout)
    {
        await WaitForCountAsync(() => binaryMessages.Count >= expectedCount, timeout);
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await ReceiveLoopAsync(webSocket, context.RequestAborted);
    }

    private async Task ReceiveLoopAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(webSocket, cancellationToken);
            if (message is null)
            {
                break;
            }

            if (message.Value.MessageType == WebSocketMessageType.Text)
            {
                var text = Encoding.UTF8.GetString(message.Value.Payload);
                textMessages.Enqueue(text);

                var reply = textReplyFactory(text);
                if (!string.IsNullOrWhiteSpace(reply) && webSocket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
                }

                continue;
            }

            if (message.Value.MessageType == WebSocketMessageType.Binary)
            {
                binaryMessages.Enqueue(message.Value.Payload);
            }
        }
    }

    private static async Task WaitForCountAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            timeoutCts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, timeoutCts.Token);
        }
    }

    private static async Task<(WebSocketMessageType MessageType, byte[] Payload)?> ReceiveMessageAsync(
        WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }

            if (result.EndOfMessage)
            {
                return (result.MessageType, stream.ToArray());
            }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
