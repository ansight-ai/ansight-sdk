using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingSessionConnector
{
    private readonly Func<PairingWifiPreflightStatus> wifiStatusProvider;

    public PairingSessionConnector()
        : this(PairingWifiPreflight.GetStatus)
    {
    }

    internal PairingSessionConnector(Func<PairingWifiPreflightStatus> wifiStatusProvider)
    {
        this.wifiStatusProvider = wifiStatusProvider ?? throw new ArgumentNullException(nameof(wifiStatusProvider));
    }

    public async Task<PairingConnectionAttempt> ConnectAsync(
        PairingConfig config,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (!TryResolveManualHostAddress(options?.ManualHostAddress, out var hostAddress))
        {
            return PairingConnectionAttempt.FromFailure(
                "A current host address is required. Import a fresh Studio QR code or enter the host IP manually.",
                PairingFailureCodes.HostAddressRequired);
        }

        if (wifiStatusProvider() == PairingWifiPreflightStatus.NotConnected)
        {
            const string wifiRequiredMessage = "Ansight is unavailable because this device is not connected to Wi-Fi.";

            HostPairingProgressReporter.Report(
                progress,
                HostPairingProgressKind.Connection,
                wifiRequiredMessage,
                source: HostPairingSource.HostConnection,
                reasonCode: PairingFailureCodes.WifiRequired);

            return PairingConnectionAttempt.FromFailure(
                wifiRequiredMessage,
                PairingFailureCodes.WifiRequired);
        }

        var discoveryMode = options?.DiscoveryMode ?? PairingDiscoveryMode.ConfiguredHint;
        HostPairingProgressReporter.Report(
            progress,
            HostPairingProgressKind.Connection,
            discoveryMode == PairingDiscoveryMode.BasicManual
                ? $"Using manual host address: {hostAddress}"
                : $"Using configured host hint: {hostAddress}",
            source: HostPairingSource.HostConnection);
        HostPairingProgressReporter.Report(
            progress,
            HostPairingProgressKind.Connection,
            $"Connecting to host at {hostAddress}:{config.Host.DiscoveryPort}",
            source: HostPairingSource.HostConnection);

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        ConnectResponse? connectResponse;
        try
        {
            connectResponse = await SendConnectRequestAsync(config, clientName, hostAddress!, connectTimeout.Token);
        }
        catch (SocketException ex)
        {
            return PairingConnectionAttempt.FromFailure(
                $"UDP connect failed: {ex.Message}",
                PairingFailureCodes.UdpBootstrapFailed);
        }

        if (connectResponse is null)
        {
            return PairingConnectionAttempt.FromFailure(
                "No connect response from host. The remembered host address may be stale. Import a fresh Studio QR code or enter the host IP manually.",
                PairingFailureCodes.UdpBootstrapTimeout);
        }

        HostPairingProgressReporter.Report(
            progress,
            HostPairingProgressKind.Connection,
            $"Host response: {connectResponse.Message}",
            source: HostPairingSource.HostConnection,
            reasonCode: connectResponse.Reason);
        if (!string.IsNullOrWhiteSpace(connectResponse.ReasonMessage))
        {
            HostPairingProgressReporter.Report(
                progress,
                HostPairingProgressKind.Connection,
                $"Reason: {connectResponse.ReasonMessage}",
                source: HostPairingSource.HostConnection,
                reasonCode: connectResponse.Reason);
        }

        HostPairingProgressReporter.Report(
            progress,
            HostPairingProgressKind.Connection,
            $"Reason code: {connectResponse.Reason}",
            source: HostPairingSource.HostConnection,
            reasonCode: connectResponse.Reason);
        HostPairingProgressReporter.Report(
            progress,
            HostPairingProgressKind.Connection,
            $"Accepted: {connectResponse.Accepted}",
            source: HostPairingSource.HostConnection,
            reasonCode: connectResponse.Reason);

        if (!connectResponse.Accepted)
        {
            return PairingConnectionAttempt.FromRejected(hostAddress!, connectResponse);
        }

        if (connectResponse.WebSocketPort is null ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketPath) ||
            string.IsNullOrWhiteSpace(connectResponse.WebSocketToken))
        {
            return PairingConnectionAttempt.FromFailure(
                "Host did not provide a WebSocket handoff.",
                PairingFailureCodes.WebSocketHandoffUnavailable);
        }

        var wsUri = BuildWebSocketUri(
            hostAddress!,
            connectResponse.WebSocketPort.Value,
            connectResponse.WebSocketPath,
            connectResponse.WebSocketToken);
        HostPairingProgressReporter.Report(
            progress,
            HostPairingProgressKind.Connection,
            $"Opening WebSocket: {wsUri}",
            source: HostPairingSource.Transport);

        var connectedSocket = await ConnectWebSocketWithRetryAsync(wsUri, cancellationToken);
        if (connectedSocket is null)
        {
            return PairingConnectionAttempt.FromFailure(
                "WebSocket endpoint did not become reachable in time.",
                PairingFailureCodes.WebSocketEndpointUnreachable);
        }

        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(10));

            var hostHello = await PairingSessionTransport.ReceiveTextAsync(connectedSocket, helloTimeout.Token);
            HostPairingProgressReporter.Report(
                progress,
                HostPairingProgressKind.Transport,
                $"WS <- {hostHello}",
                isVerbose: true,
                source: HostPairingSource.Transport);

            return PairingConnectionAttempt.FromSuccess(hostAddress!, connectResponse, hostHello, connectedSocket);
        }
        catch (Exception ex)
        {
            connectedSocket.Dispose();
            return PairingConnectionAttempt.FromFailure(
                $"WebSocket handshake failed: {ex.Message}",
                PairingFailureCodes.WebSocketHandshakeFailed);
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
        using var udpClient = new UdpClient(hostAddress.AddressFamily);

        var request = new ConnectRequest
        {
            Type = "CONNECT_REQ",
            Ver = 1,
            ConfigId = config.ConfigId,
            OneTimeToken = config.OneTimeToken,
            AppId = config.AppId,
            ClientName = clientName,
            ProcessSessionId = ProcessSessionIdentity.Current
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

    private static Uri BuildWebSocketUri(IPAddress hostAddress, int port, string path, string token)
    {
        var builder = new UriBuilder(Uri.UriSchemeWs, hostAddress.ToString(), port)
        {
            Path = path,
            Query = $"token={Uri.EscapeDataString(token)}"
        };
        return builder.Uri;
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
    ClientWebSocket? WebSocket,
    string? FailureCode)
{
    public static PairingConnectionAttempt FromFailure(string message, string? failureCode = null)
        => new(false, false, message, null, null, null, null, failureCode);

    public static PairingConnectionAttempt FromRejected(IPAddress hostAddress, ConnectResponse connectResponse)
        => new(false, false, connectResponse.ReasonMessage ?? connectResponse.Message, hostAddress, connectResponse, null, null, null);

    public static PairingConnectionAttempt FromSuccess(
        IPAddress hostAddress,
        ConnectResponse connectResponse,
        string hostHello,
        ClientWebSocket webSocket)
        => new(true, true, "Connected to host and WebSocket session is ready.", hostAddress, connectResponse, hostHello, webSocket, null);
}
