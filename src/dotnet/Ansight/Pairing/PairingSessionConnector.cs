using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingSessionConnector
{
    private readonly IPairingHostDiscoveryStrategy? _hostDiscoveryStrategy;

    public PairingSessionConnector(IPairingHostDiscoveryStrategy? hostDiscoveryStrategy)
    {
        _hostDiscoveryStrategy = hostDiscoveryStrategy;
    }

    public async Task<PairingConnectionAttempt> ConnectAsync(
        PairingConfig config,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        IPAddress? discoveredHostAddress;
        var discoveryMode = options?.DiscoveryMode ?? PairingDiscoveryMode.ConfiguredStrategy;

        if (discoveryMode == PairingDiscoveryMode.BasicManual)
        {
            if (!TryResolveManualHostAddress(options?.ManualHostAddress, out var manualHostAddress))
            {
                return PairingConnectionAttempt.FromFailure("Basic manual discovery requires a valid host IP address.");
            }

            progress?.Report($"Using manual host address: {manualHostAddress}");
            discoveredHostAddress = manualHostAddress!;
        }
        else
        {
            if (_hostDiscoveryStrategy is null)
            {
                return PairingConnectionAttempt.FromFailure("No host discovery strategy was configured for automatic pairing.");
            }

            using var discoverTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            discoverTimeout.CancelAfter(TimeSpan.FromSeconds(8));

            try
            {
                discoveredHostAddress = await _hostDiscoveryStrategy.DiscoverHostAsync(config, discoverTimeout.Token);
            }
            catch (SocketException ex)
            {
                return PairingConnectionAttempt.FromFailure($"Discovery strategy failed: {ex.Message}");
            }
        }

        if (discoveredHostAddress is null)
        {
            return PairingConnectionAttempt.FromFailure("No host discovered.");
        }

        progress?.Report($"Discovered host at {discoveredHostAddress}:{config.Host.DiscoveryPort}");

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        ConnectResponse? connectResponse;
        try
        {
            connectResponse = await SendConnectRequestAsync(config, clientName, discoveredHostAddress, connectTimeout.Token);
        }
        catch (SocketException ex)
        {
            return PairingConnectionAttempt.FromFailure($"UDP connect failed: {ex.Message}");
        }

        if (connectResponse is null)
        {
            return PairingConnectionAttempt.FromFailure("No connect response from host.");
        }

        progress?.Report($"Host response: {connectResponse.Message}");
        if (!string.IsNullOrWhiteSpace(connectResponse.ReasonMessage))
        {
            progress?.Report($"Reason: {connectResponse.ReasonMessage}");
        }

        progress?.Report($"Reason code: {connectResponse.Reason}");
        progress?.Report($"Accepted: {connectResponse.Accepted}");

        if (!connectResponse.Accepted)
        {
            return PairingConnectionAttempt.FromRejected(discoveredHostAddress, connectResponse);
        }

        if (connectResponse.WebSocketPort is null ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketPath) ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketToken))
        {
            return PairingConnectionAttempt.FromFailure("Host did not provide a WebSocket handoff.");
        }

        var wsUri = new Uri(
            $"ws://{discoveredHostAddress}:{connectResponse.WebSocketPort}{connectResponse.WebSocketPath}?token={Uri.EscapeDataString(connectResponse.WebSocketToken)}");
        progress?.Report($"Opening WebSocket: {wsUri}");

        var connectedSocket = await ConnectWebSocketWithRetryAsync(wsUri, cancellationToken);
        if (connectedSocket is null)
        {
            return PairingConnectionAttempt.FromFailure("WebSocket endpoint did not become reachable in time.");
        }

        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            var hostHello = await PairingSessionTransport.ReceiveTextAsync(connectedSocket, helloTimeout.Token);
            progress?.Report($"WS <- {hostHello}");

            return PairingConnectionAttempt.FromSuccess(discoveredHostAddress, connectResponse, hostHello, connectedSocket);
        }
        catch (Exception ex)
        {
            connectedSocket.Dispose();
            return PairingConnectionAttempt.FromFailure($"WebSocket handshake failed: {ex.Message}");
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

    private static async Task<ConnectResponse?> SendConnectRequestAsync(
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
        await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(hostAddress, config.Host.DiscoveryPort));

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

    private static bool TryResolveManualHostAddress(string? manualHostAddress, out IPAddress? hostAddress)
    {
        hostAddress = null;
        if (string.IsNullOrWhiteSpace(manualHostAddress))
        {
            return false;
        }

        return IPAddress.TryParse(manualHostAddress.Trim(), out hostAddress);
    }
}

internal sealed record PairingConnectionAttempt(
    bool Success,
    bool Accepted,
    string Message,
    IPAddress? HostAddress,
    ConnectResponse? ConnectResponse,
    string? HostHello,
    ClientWebSocket? WebSocket)
{
    public static PairingConnectionAttempt FromFailure(string message)
        => new(false, false, message, null, null, null, null);

    public static PairingConnectionAttempt FromRejected(IPAddress hostAddress, ConnectResponse connectResponse)
        => new(false, false, connectResponse.ReasonMessage ?? connectResponse.Message, hostAddress, connectResponse, null, null);

    public static PairingConnectionAttempt FromSuccess(
        IPAddress hostAddress,
        ConnectResponse connectResponse,
        string hostHello,
        ClientWebSocket webSocket)
        => new(true, true, "Connected to host and WebSocket session is ready.", hostAddress, connectResponse, hostHello, webSocket);
}
