using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Ansight.Pairing;

internal sealed class PairingSessionTransport : IDisposable
{
    private ClientWebSocket? webSocket;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly SemaphoreSlim requestLock = new(1, 1);
    private CancellationTokenSource? receivePumpCts;
    private Task? receivePumpTask;
    private Channel<string>? incomingMessages;
    private bool disposed;

    public bool IsOpen => webSocket is { State: WebSocketState.Open };

    public void Attach(ClientWebSocket webSocket)
    {
        ArgumentNullException.ThrowIfNull(webSocket);

        this.webSocket = webSocket;
        StartReceivePump(webSocket);
    }

    public async Task<OperationResult> SendRequestAsync(
        string payload,
        string? outboundProgressMessage,
        string successMessage,
        string failurePrefix,
        IProgress<string>? progress,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken)
    {
        var webSocket = this.webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        try
        {
            await requestLock.WaitAsync(cancellationToken);
            try
            {
                await SendPayloadAsync(webSocket, payload, cancellationToken);
                progress?.Report(outboundProgressMessage ?? $"WS -> {payload}");

                using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ackTimeout.CancelAfter(acknowledgementTimeout);
                var hostAck = await ReceiveInboundMessageAsync(ackTimeout.Token);
                progress?.Report($"WS <- {hostAck}");

                if (string.Equals(hostAck, "<close>", StringComparison.Ordinal))
                {
                    await CloseAsync(CancellationToken.None);
                    return OperationResult.FromFailure("Host closed the WebSocket session.");
                }
            }
            finally
            {
                requestLock.Release();
            }

            return OperationResult.FromSuccess(successMessage);
        }
        catch (Exception ex)
        {
            await CloseAsync(CancellationToken.None);
            return OperationResult.FromFailure($"{failurePrefix}: {ex.Message}");
        }
    }

    public async Task<OperationResult> SendTextAsync(string payload, CancellationToken cancellationToken)
    {
        var webSocket = this.webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        try
        {
            await SendPayloadAsync(webSocket, payload, cancellationToken);
            return OperationResult.FromSuccess("Payload sent.");
        }
        catch (Exception ex)
        {
            return OperationResult.FromFailure($"Failed to send WebSocket payload: {ex.Message}");
        }
    }

    public async Task<OperationResult> SendBinaryAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        var webSocket = this.webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        try
        {
            await SendPayloadAsync(webSocket, payload, messageType, cancellationToken);
            return OperationResult.FromSuccess("Payload sent.");
        }
        catch (Exception ex)
        {
            return OperationResult.FromFailure($"Failed to send WebSocket payload: {ex.Message}");
        }
    }

    public async Task<OperationResult> CloseAsync(CancellationToken cancellationToken)
    {
        var webSocket = this.webSocket;
        var receivePumpCts = this.receivePumpCts;
        var receivePumpTask = this.receivePumpTask;
        var incomingMessages = this.incomingMessages;

        this.webSocket = null;
        this.receivePumpCts = null;
        this.receivePumpTask = null;
        this.incomingMessages = null;

        receivePumpCts?.Cancel();

        if (webSocket is null)
        {
            incomingMessages?.Writer.TryWrite("<close>");
            incomingMessages?.Writer.TryComplete();
            receivePumpCts?.Dispose();
            return OperationResult.FromSuccess("Session already closed.");
        }

        try
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                using var closeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                closeTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client session closed.", closeTimeout.Token);
            }
        }
        catch
        {
            // Ignore close errors; socket is still disposed.
        }
        finally
        {
            webSocket.Dispose();
        }

        if (receivePumpTask is not null)
        {
            try
            {
                await receivePumpTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch
            {
                // Ignore receive pump errors while stopping.
            }
        }

        receivePumpCts?.Dispose();
        incomingMessages?.Writer.TryWrite("<close>");
        incomingMessages?.Writer.TryComplete();

        return OperationResult.FromSuccess("Session disconnected.");
    }

    public static async Task<string> ReceiveTextAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return "<close>";
            }

            if (result.Count > 0)
            {
                stream.Write(buffer, 0, result.Count);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        receivePumpCts?.Cancel();
        receivePumpCts?.Dispose();
        webSocket?.Dispose();
        incomingMessages?.Writer.TryComplete();
        sendLock.Dispose();
        requestLock.Dispose();
        webSocket = null;
        receivePumpTask = null;
        receivePumpCts = null;
        incomingMessages = null;
    }

    private void StartReceivePump(ClientWebSocket webSocket)
    {
        incomingMessages = System.Threading.Channels.Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleWriter = true
        });
        receivePumpCts = new CancellationTokenSource();
        receivePumpTask = Task.Run(() => RunReceivePumpAsync(webSocket, incomingMessages.Writer, receivePumpCts.Token));
    }

    private async Task RunReceivePumpAsync(
        ClientWebSocket webSocket,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string message;

                try
                {
                    message = await ReceiveTextAsync(webSocket, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await writer.WriteAsync("<close>", CancellationToken.None);
                    break;
                }

                if (string.Equals(message, "<close>", StringComparison.Ordinal))
                {
                    await writer.WriteAsync(message, CancellationToken.None);
                    break;
                }

                if (await PairingToolProtocolProcessor.TryHandleIncomingMessageAsync(
                        webSocket,
                        message,
                        SendPayloadAsync,
                        cancellationToken))
                {
                    continue;
                }

                await writer.WriteAsync(message, cancellationToken);
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task<string> ReceiveInboundMessageAsync(CancellationToken cancellationToken)
    {
        var incomingMessages = this.incomingMessages;
        if (incomingMessages is null)
        {
            return "<close>";
        }

        try
        {
            return await incomingMessages.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return "<close>";
        }
    }

    private async Task SendPayloadAsync(ClientWebSocket webSocket, string payload, CancellationToken cancellationToken)
    {
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await SendTextAsync(webSocket, payload, sendTimeout.Token);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private async Task SendPayloadAsync(
        ClientWebSocket webSocket,
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await SendBinaryAsync(webSocket, payload, messageType, sendTimeout.Token);
        }
        finally
        {
            sendLock.Release();
    }
    }

    private static async Task SendTextAsync(ClientWebSocket webSocket, string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);
        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task SendBinaryAsync(
        ClientWebSocket webSocket,
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        await webSocket.SendAsync(payload, messageType, WebSocketMessageFlags.EndOfMessage, cancellationToken);
    }
}
