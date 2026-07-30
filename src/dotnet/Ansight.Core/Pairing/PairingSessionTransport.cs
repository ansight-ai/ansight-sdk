using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ansight.Pairing;

internal delegate Task BinaryFragmentSender(
    ReadOnlyMemory<byte> payload,
    bool endOfMessage,
    CancellationToken cancellationToken);

internal sealed record PairingControlRequestResult(
    OperationResult OperationResult,
    PairingControlEnvelope? Response);

internal sealed class PairingSessionTransport : IPairingBinaryTransport, IDisposable
{
    private ClientWebSocket? webSocket;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly SemaphoreSlim requestLock = new(1, 1);
    private readonly Lock responseGate = new();
    private readonly Dictionary<string, TaskCompletionSource<PairingControlEnvelope>> pendingResponses = new(StringComparer.Ordinal);
    private CancellationTokenSource? receivePumpCts;
    private Task? receivePumpTask;
    private int closeNotificationState;
    private bool disposed;

    public bool IsOpen => webSocket is { State: WebSocketState.Open };

    internal event EventHandler? Closed;

    public void Attach(ClientWebSocket webSocket)
    {
        ArgumentNullException.ThrowIfNull(webSocket);

        this.webSocket = webSocket;
        Interlocked.Exchange(ref closeNotificationState, 0);
        StartReceivePump(webSocket);
    }

    public async Task<OperationResult> SendControlRequestAsync(
        string action,
        JsonObject? payload,
        string? outboundProgressMessage,
        string successMessage,
        string failurePrefix,
        IProgress<HostConnectionProgressUpdate>? progress,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken,
        HostConnectionSource source = HostConnectionSource.Transport,
        HostConnectionProgressKind kind = HostConnectionProgressKind.Transport)
    {
        var result = await SendControlRequestWithResponseAsync(
            action,
            payload,
            outboundProgressMessage,
            successMessage,
            failurePrefix,
            progress,
            acknowledgementTimeout,
            cancellationToken,
            source,
            kind);
        return result.OperationResult;
    }

    internal async Task<PairingControlRequestResult> SendControlRequestWithResponseAsync(
        string action,
        JsonObject? payload,
        string? outboundProgressMessage,
        string successMessage,
        string failurePrefix,
        IProgress<HostConnectionProgressUpdate>? progress,
        TimeSpan acknowledgementTimeout,
        CancellationToken cancellationToken,
        HostConnectionSource source = HostConnectionSource.Transport,
        HostConnectionProgressKind kind = HostConnectionProgressKind.Transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var webSocket = this.webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return new PairingControlRequestResult(
                OperationResult.FromFailure("WebSocket session is not open."),
                null);
        }

        var requestId = $"client.{Guid.NewGuid():N}";
        var envelope = new PairingControlEnvelope
        {
            Type = PairingControlEnvelope.RequestType,
            Id = requestId,
            Action = action.Trim(),
            Payload = payload
        };
        var responseSource = new TaskCompletionSource<PairingControlEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (responseGate)
        {
            pendingResponses[requestId] = responseSource;
        }

        try
        {
            await requestLock.WaitAsync(cancellationToken);
            try
            {
                var messageJson = JsonSerializer.Serialize(envelope, PairingJson.Compact);
                await SendPayloadAsync(webSocket, messageJson, cancellationToken);
                HostPairingProgressReporter.Report(
                    progress,
                    kind,
                    outboundProgressMessage ?? $"WS -> {action}",
                    isVerbose: true,
                    source: source);
            }
            finally
            {
                requestLock.Release();
            }

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseTimeout.CancelAfter(acknowledgementTimeout);
            var response = await responseSource.Task.WaitAsync(responseTimeout.Token);
            HostPairingProgressReporter.Report(
                progress,
                kind,
                $"WS <- {response.Action}: {response.Message ?? (response.Success ? "ok" : "failed")}",
                isVerbose: true,
                source: source);

            if (!response.Success)
            {
                return new PairingControlRequestResult(
                    OperationResult.FromFailure($"{failurePrefix}: {response.Message ?? "request failed"}"),
                    response);
            }

            return new PairingControlRequestResult(
                OperationResult.FromSuccess(string.IsNullOrWhiteSpace(response.Message) ? successMessage : response.Message!),
                response);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            await CloseAsync(CancellationToken.None);
            return new PairingControlRequestResult(
                OperationResult.FromFailure($"{failurePrefix}: {ex.Message}"),
                null);
        }
        catch (Exception ex)
        {
            await CloseAsync(CancellationToken.None);
            return new PairingControlRequestResult(
                OperationResult.FromFailure($"{failurePrefix}: {ex.Message}"),
                null);
        }
        finally
        {
            lock (responseGate)
            {
                pendingResponses.Remove(requestId);
            }
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

    public async Task<OperationResult> SendBinaryAsync(
        Func<BinaryFragmentSender, CancellationToken, Task> payloadWriter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloadWriter);

        var webSocket = this.webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        try
        {
            await sendLock.WaitAsync(cancellationToken);
            try
            {
                using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await payloadWriter(
                    (payload, endOfMessage, fragmentCancellationToken) => SendBinaryFragmentAsync(
                        webSocket,
                        payload,
                        WebSocketMessageType.Binary,
                        endOfMessage,
                        fragmentCancellationToken),
                    sendTimeout.Token);
                return OperationResult.FromSuccess("Payload sent.");
            }
            finally
            {
                sendLock.Release();
            }
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
        var hadSession = webSocket is not null || receivePumpTask is not null || receivePumpCts is not null;

        this.webSocket = null;
        this.receivePumpCts = null;
        this.receivePumpTask = null;

        receivePumpCts?.Cancel();
        FailPendingResponses("WebSocket session closed.");

        if (webSocket is null)
        {
            receivePumpCts?.Dispose();
            if (hadSession)
            {
                NotifyClosed();
            }

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
        if (hadSession)
        {
            NotifyClosed();
        }

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
        sendLock.Dispose();
        requestLock.Dispose();
        FailPendingResponses("WebSocket session disposed.");
        webSocket = null;
        receivePumpTask = null;
        receivePumpCts = null;
    }

    private void StartReceivePump(ClientWebSocket webSocket)
    {
        receivePumpCts = new CancellationTokenSource();
        receivePumpTask = Task.Run(() => RunReceivePumpAsync(webSocket, receivePumpCts.Token));
    }

    private async Task RunReceivePumpAsync(
        ClientWebSocket webSocket,
        CancellationToken cancellationToken)
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
                NotifyClosed();
                break;
            }

            if (string.Equals(message, "<close>", StringComparison.Ordinal))
            {
                NotifyClosed();
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

            if (TryHandleControlResponse(message))
            {
                continue;
            }

            Logger.Warning($"Ignoring unexpected pairing socket message: {message}");
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
            await SendBinaryFragmentAsync(webSocket, payload, messageType, endOfMessage: true, sendTimeout.Token);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task SendTextAsync(ClientWebSocket webSocket, string payload, CancellationToken cancellationToken)
    {
        var byteCount = Encoding.UTF8.GetByteCount(payload);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));
        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(payload, 0, payload.Length, buffer, 0);
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer, 0, bytesWritten),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task SendBinaryFragmentAsync(
        ClientWebSocket webSocket,
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        await webSocket.SendAsync(
            payload,
            messageType,
            endOfMessage ? WebSocketMessageFlags.EndOfMessage : default,
            cancellationToken);
    }

    private void NotifyClosed()
    {
        if (Interlocked.Exchange(ref closeNotificationState, 1) != 0)
        {
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryHandleControlResponse(string message)
    {
        PairingControlEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PairingControlEnvelope>(message, PairingJson.Compact);
        }
        catch (JsonException)
        {
            return false;
        }

        if (envelope is null ||
            !string.Equals(envelope.Type, PairingControlEnvelope.ResponseType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(envelope.ReplyTo))
        {
            return false;
        }

        TaskCompletionSource<PairingControlEnvelope>? responseSource;
        lock (responseGate)
        {
            if (!pendingResponses.TryGetValue(envelope.ReplyTo, out responseSource))
            {
                return false;
            }

            pendingResponses.Remove(envelope.ReplyTo);
        }

        return responseSource.TrySetResult(envelope);
    }

    private void FailPendingResponses(string reason)
    {
        TaskCompletionSource<PairingControlEnvelope>[] responseSources;
        lock (responseGate)
        {
            responseSources = pendingResponses.Values.ToArray();
            pendingResponses.Clear();
        }

        if (responseSources.Length == 0)
        {
            return;
        }

        var exception = new IOException(reason);
        foreach (var responseSource in responseSources)
        {
            responseSource.TrySetException(exception);
        }
    }
}
