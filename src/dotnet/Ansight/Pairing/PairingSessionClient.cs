using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ansight.Pairing;

public sealed class PairingSessionClient : IDisposable
{
    private ClientWebSocket? _webSocket;
    private ConnectResponse? _connectResponse;
    private bool _disposed;

    public bool TryParseAndValidateConfig(string configJson, string? expectedAppId, out PairingConfig? config, out string error)
    {
        config = null;

        if (string.IsNullOrWhiteSpace(configJson))
        {
            error = "Paste or load a pairing config.";
            return false;
        }

        try
        {
            config = JsonSerializer.Deserialize<PairingConfig>(configJson, PairingJson.Compact);
        }
        catch (Exception ex)
        {
            error = $"Failed to parse config JSON: {ex.Message}";
            return false;
        }

        if (config is null)
        {
            error = "Config JSON is empty.";
            return false;
        }

        return TryValidateConfig(config, expectedAppId, out error);
    }

    public bool TryValidateConfig(PairingConfig config, string? expectedAppId, out string error)
    {
        if (!VerifyPairingConfigSignature(config))
        {
            error = "Connection config signature is invalid.";
            return false;
        }

        if (DateTimeOffset.UtcNow > config.ExpiresAt)
        {
            error = $"Connection config expired at {config.ExpiresAt:O}.";
            return false;
        }

        if (!ValidateAppId(config, expectedAppId, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public async Task<OpenSessionResult> OpenSessionAsync(
        PairingConfig config,
        string clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await CloseSessionAsync(CancellationToken.None);

        progress?.Report($"Config validated. ConfigId: {config.ConfigId}");

        using var discoverTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoverTimeout.CancelAfter(TimeSpan.FromSeconds(8));

        DiscoveredHost? discoveredHost;
        try
        {
            discoveredHost = await DiscoverHostAsync(config, discoverTimeout.Token);
        }
        catch (SocketException ex)
        {
            return OpenSessionResult.FromFailure($"UDP discovery failed: {ex.Message}");
        }

        if (discoveredHost is null)
        {
            return OpenSessionResult.FromFailure("No host discovered.");
        }

        progress?.Report($"Discovered host at {discoveredHost.Address}:{PairingProtocolDefaults.DiscoveryPort}");

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        ConnectResponse? connectResponse;
        try
        {
            connectResponse = await ConnectAsync(config, clientName, discoveredHost.Address, connectTimeout.Token);
        }
        catch (SocketException ex)
        {
            return OpenSessionResult.FromFailure($"UDP connect failed: {ex.Message}");
        }

        if (connectResponse is null)
        {
            return OpenSessionResult.FromFailure("No connect response from host.");
        }

        progress?.Report($"Host response: {connectResponse.Message}");
        progress?.Report($"Reason: {connectResponse.Reason}");
        progress?.Report($"Accepted: {connectResponse.Accepted}");

        if (!connectResponse.Accepted)
        {
            return OpenSessionResult.FromRejected("Host rejected the connection request.", discoveredHost.Address, connectResponse);
        }

        if (connectResponse.WebSocketPort is null ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketPath) ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketToken))
        {
            return OpenSessionResult.FromFailure("Host did not provide a WebSocket handoff.");
        }

        var wsUri = new Uri(
            $"ws://{discoveredHost.Address}:{connectResponse.WebSocketPort}{connectResponse.WebSocketPath}?token={Uri.EscapeDataString(connectResponse.WebSocketToken)}");
        progress?.Report($"Opening WebSocket: {wsUri}");

        var connectedSocket = await ConnectWebSocketWithRetryAsync(wsUri, cancellationToken);
        if (connectedSocket is null)
        {
            return OpenSessionResult.FromFailure("WebSocket endpoint did not become reachable in time.");
        }

        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            var hostHello = await ReceiveTextAsync(connectedSocket, helloTimeout.Token);
            progress?.Report($"WS <- {hostHello}");

            _webSocket = connectedSocket;
            _connectResponse = connectResponse;

            return OpenSessionResult.FromSuccess("Connected to host and WebSocket session is ready.", discoveredHost.Address, connectResponse, hostHello);
        }
        catch (Exception ex)
        {
            connectedSocket.Dispose();
            return OpenSessionResult.FromFailure($"WebSocket handshake failed: {ex.Message}");
        }
    }

    public async Task<OperationResult> SendClientLogAsync(string logLine, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        if (string.IsNullOrWhiteSpace(logLine))
        {
            return OperationResult.FromFailure("Enter log text before sending.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_LOG",
            sentAtUtc = DateTimeOffset.UtcNow,
            data = logLine.Trim()
        }, PairingJson.Compact);

        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await SendTextAsync(webSocket, payload, sendTimeout.Token);
            progress?.Report($"WS -> {payload}");

            using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ackTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            var hostAck = await ReceiveTextAsync(webSocket, ackTimeout.Token);
            progress?.Report($"WS <- {hostAck}");

            if (string.Equals(hostAck, "<close>", StringComparison.Ordinal))
            {
                await CloseSessionAsync(CancellationToken.None);
                return OperationResult.FromFailure("Host closed the WebSocket session.");
            }

            return OperationResult.FromSuccess("Log sent.");
        }
        catch (Exception ex)
        {
            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromFailure($"Failed to send log: {ex.Message}");
        }
    }

    public async Task<OperationResult> CompleteSessionAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;
        if (webSocket is null || webSocket.State != WebSocketState.Open)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            source = "client",
            type = "CLIENT_DONE",
            sentAtUtc = DateTimeOffset.UtcNow,
            data = "client log stream complete"
        }, PairingJson.Compact);

        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await SendTextAsync(webSocket, payload, sendTimeout.Token);
            progress?.Report($"WS -> {payload}");

            using var ackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ackTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var doneAck = await ReceiveTextAsync(webSocket, ackTimeout.Token);
            progress?.Report($"WS <- {doneAck}");

            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromSuccess("Session complete.");
        }
        catch (Exception ex)
        {
            await CloseSessionAsync(CancellationToken.None);
            return OperationResult.FromFailure($"Failed to complete session: {ex.Message}");
        }
    }

    public async Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
    {
        var webSocket = _webSocket;

        _webSocket = null;
        _connectResponse = null;

        if (webSocket is null)
        {
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

        return OperationResult.FromSuccess("Session disconnected.");
    }

    private static bool VerifyPairingConfigSignature(PairingConfig config)
    {
        try
        {
            var publicKey = Convert.FromBase64String(config.Host.HostPubKey);
            var signature = Convert.FromBase64String(config.Signature);

            using var hostKey = ECDsa.Create();
            hostKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            var signables = new[]
            {
                PairingCanonicalJson.SerializePairingConfigForSignature(config),
                PairingCanonicalJson.SerializePairingConfigForSignatureWithoutHostIdentity(config)
            };

            foreach (var signable in signables)
            {
                if (hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValidateAppId(PairingConfig config, string? expectedAppId, out string error)
    {
        var configuredAppId = config.AppId?.Trim() ?? string.Empty;
        var normalizedExpected = expectedAppId?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            error = string.Empty;
            return true;
        }

        if (!string.Equals(configuredAppId, normalizedExpected, StringComparison.Ordinal))
        {
            error = $"Config appId '{configuredAppId}' does not match expected app id '{normalizedExpected}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool VerifyDiscoverResponseSignature(DiscoverResponse response, string hostPubKeyBase64)
    {
        try
        {
            var publicKey = Convert.FromBase64String(hostPubKeyBase64);
            var signature = Convert.FromBase64String(response.Sig);

            using var hostKey = ECDsa.Create();
            hostKey.ImportSubjectPublicKeyInfo(publicKey, out _);

            var signables = new[]
            {
                PairingCanonicalJson.SerializeDiscoverResponseForSignature(response),
                PairingCanonicalJson.SerializeDiscoverResponseForSignatureWithoutHostIdentity(response)
            };

            foreach (var signable in signables)
            {
                if (hostKey.VerifyData(Encoding.UTF8.GetBytes(signable), signature, HashAlgorithmName.SHA256))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<ClientWebSocket?> ConnectWebSocketWithRetryAsync(Uri wsUri, CancellationToken cancellationToken)
    {
        const int maxAttempts = 12;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var webSocket = new ClientWebSocket();

            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                await webSocket.ConnectAsync(wsUri, connectTimeout.Token);

                if (webSocket.State == WebSocketState.Open)
                {
                    return webSocket;
                }
            }
            catch when (attempt < maxAttempts)
            {
                // Retry.
            }
            catch
            {
                webSocket.Dispose();
                throw;
            }

            webSocket.Dispose();

            if (attempt < maxAttempts)
            {
                await Task.Delay(250, cancellationToken);
            }
        }

        return null;
    }

    private static async Task<DiscoveredHost?> DiscoverHostAsync(PairingConfig config, CancellationToken cancellationToken)
    {
        var nonce = PairingCrypto.CreateBase64UrlRandom(16);
        var request = new DiscoverRequest
        {
            Type = "DISCOVER_REQ",
            Ver = 1,
            Nonce = nonce,
            AppId = config.AppId
        };

        using var udpClient = new UdpClient(0);
        udpClient.EnableBroadcast = true;

        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, PairingJson.Compact);
        var targets = config.Trust.AllowLanDiscovery
            ? new[] { IPAddress.Broadcast, IPAddress.Loopback }
            : new[] { IPAddress.Loopback };

        foreach (var target in targets)
        {
            await udpClient.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(target, PairingProtocolDefaults.DiscoveryPort));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult receiveResult;
            try
            {
                receiveResult = await udpClient.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            DiscoverResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<DiscoverResponse>(receiveResult.Buffer, PairingJson.Compact);
            }
            catch
            {
                continue;
            }

            if (response is null || !string.Equals(response.Type, "DISCOVER_RESP", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(response.RespNonce, nonce, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(response.HostPubKey, config.Host.HostPubKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!VerifyDiscoverResponseSignature(response, config.Host.HostPubKey))
            {
                continue;
            }

            return new DiscoveredHost(receiveResult.RemoteEndPoint.Address);
        }

        return null;
    }

    private static async Task<ConnectResponse?> ConnectAsync(
        PairingConfig config,
        string clientName,
        IPAddress hostAddress,
        CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient(0);

        var request = new ConnectRequest
        {
            Type = "CONNECT_REQ",
            Ver = 1,
            ConfigId = config.ConfigId,
            OneTimeToken = config.OneTimeToken,
            AppId = config.AppId,
            ClientName = clientName
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, PairingJson.Compact);
        await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(hostAddress, PairingProtocolDefaults.DiscoveryPort));

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult receiveResult;
            try
            {
                receiveResult = await udpClient.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (!Equals(receiveResult.RemoteEndPoint.Address, hostAddress))
            {
                continue;
            }

            try
            {
                var response = JsonSerializer.Deserialize<ConnectResponse>(receiveResult.Buffer, PairingJson.Compact);
                if (response is not null && string.Equals(response.Type, "CONNECT_RESP", StringComparison.Ordinal))
                {
                    return response;
                }
            }
            catch
            {
                // Ignore malformed packets.
            }
        }

        return null;
    }

    private static async Task SendTextAsync(ClientWebSocket webSocket, string payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);
        await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _webSocket?.Dispose();
        _webSocket = null;
        _connectResponse = null;
    }
}

public sealed record OpenSessionResult(
    bool Success,
    bool Accepted,
    string Message,
    IPAddress? HostAddress,
    ConnectResponse? ConnectResponse,
    string? HostHello)
{
    public static OpenSessionResult FromFailure(string message) => new(false, false, message, null, null, null);

    public static OpenSessionResult FromRejected(string message, IPAddress hostAddress, ConnectResponse connectResponse) =>
        new(false, false, message, hostAddress, connectResponse, null);

    public static OpenSessionResult FromSuccess(
        string message,
        IPAddress hostAddress,
        ConnectResponse connectResponse,
        string hostHello) =>
        new(true, true, message, hostAddress, connectResponse, hostHello);
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult FromSuccess(string message) => new(true, message);

    public static OperationResult FromFailure(string message) => new(false, message);
}
