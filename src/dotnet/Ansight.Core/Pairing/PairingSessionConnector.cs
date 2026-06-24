using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

namespace Ansight.Pairing;

internal sealed class PairingSessionConnector
{
    private readonly Func<PairingWifiPreflightStatus> wifiStatusProvider;
    private readonly Func<string?> simulatorLocalHostAddressProvider;

    public PairingSessionConnector()
        : this(PairingWifiPreflight.GetStatus, PairingSimulatorLocalHostAddress.Resolve)
    {
    }

    internal PairingSessionConnector(Func<PairingWifiPreflightStatus> wifiStatusProvider)
        : this(wifiStatusProvider, PairingSimulatorLocalHostAddress.Resolve)
    {
    }

    internal PairingSessionConnector(
        Func<PairingWifiPreflightStatus> wifiStatusProvider,
        Func<string?> simulatorLocalHostAddressProvider)
    {
        this.wifiStatusProvider = wifiStatusProvider ?? throw new ArgumentNullException(nameof(wifiStatusProvider));
        this.simulatorLocalHostAddressProvider = simulatorLocalHostAddressProvider
                                                 ?? throw new ArgumentNullException(nameof(simulatorLocalHostAddressProvider));
    }

    public async Task<PairingConnectionAttempt> ConnectAsync(
        ParsedPairingDocument document,
        string clientName,
        PairingConnectionOptions? options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var simulatorLocalHostAddress = ResolveSimulatorLocalHostAddress();
        var config = document.Config;
        var hostAddressCandidates = PairingDiscoveryHintHostAddresses.ResolveCandidates(
            document.DiscoveryHint,
            options?.HostAddressOverride,
            simulatorLocalHostAddress);
        if (hostAddressCandidates.Length == 0)
        {
            return PairingConnectionAttempt.FromFailure(
                "A current host address is required. Import a fresh pairing config or compact pairing config code.",
                PairingFailureCodes.HostAddressRequired);
        }

        var hostNetworkCheckMessage = BuildHostNetworkCheckMessage(document.DiscoveryHint);
        if (!HasSimulatorLocalHostCandidate(hostAddressCandidates, simulatorLocalHostAddress) &&
            wifiStatusProvider() == PairingWifiPreflightStatus.NotConnected)
        {
            var wifiRequiredMessage =
                $"Ansight is unavailable because this device is not connected to Wi-Fi. {hostNetworkCheckMessage}";

            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Connection,
                wifiRequiredMessage,
                source: HostConnectionSource.HostConnection,
                reasonCode: PairingFailureCodes.WifiRequired);

            return PairingConnectionAttempt.FromFailure(
                wifiRequiredMessage,
                PairingFailureCodes.WifiRequired);
        }

        var discoveryPort = PairingDiscoveryPortResolver.Resolve(document, options?.DiscoveryPort);
        var usingHostOverride = !string.IsNullOrWhiteSpace(options?.HostAddressOverride);
        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.Connection,
            usingHostOverride
                ? $"Using host override address: {hostAddressCandidates[0]}"
                : $"Using pairing config host address candidates: {string.Join(", ", hostAddressCandidates)}",
            source: HostConnectionSource.HostConnection);

        PairingConnectionAttempt? lastFailure = null;
        for (var index = 0; index < hostAddressCandidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hostAddressCandidate = hostAddressCandidates[index];
            if (!TryResolveHostAddress(hostAddressCandidate, out var hostAddress))
            {
                var invalidAddressMessage = $"Pairing host address '{hostAddressCandidate}' is not a valid IP address.";
                HostPairingProgressReporter.Report(
                    progress,
                    HostConnectionProgressKind.Connection,
                    invalidAddressMessage,
                    source: HostConnectionSource.HostConnection,
                    reasonCode: PairingFailureCodes.HostAddressRequired);

                lastFailure = PairingConnectionAttempt.FromFailure(
                    invalidAddressMessage,
                    PairingFailureCodes.HostAddressRequired);
                continue;
            }

            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Connection,
                hostAddressCandidates.Length == 1
                    ? $"Connecting to host at {hostAddress}:{discoveryPort}"
                    : $"Connecting to host at {hostAddress}:{discoveryPort} ({index + 1}/{hostAddressCandidates.Length})",
                source: HostConnectionSource.HostConnection);

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));

            ConnectResponse? connectResponse;
            try
            {
                connectResponse = await SendConnectRequestAsync(
                    config,
                    clientName,
                    hostAddress!,
                    discoveryPort,
                    connectTimeout.Token);
            }
            catch (SocketException ex)
            {
                lastFailure = PairingConnectionAttempt.FromFailure(
                    $"UDP connect failed for {hostAddress}: {ex.Message}",
                    PairingFailureCodes.UdpBootstrapFailed);
                continue;
            }

            if (connectResponse is null)
            {
                lastFailure = PairingConnectionAttempt.FromFailure(
                    $"No connect response from host at {hostAddress}. {hostNetworkCheckMessage} The remembered host address may be stale. Import a fresh pairing QR code or enter the host IP manually.",
                    PairingFailureCodes.UdpBootstrapTimeout);
                continue;
            }

            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Connection,
                $"Host response: {connectResponse.Message}",
                source: HostConnectionSource.HostConnection,
                reasonCode: connectResponse.Reason);
            if (!string.IsNullOrWhiteSpace(connectResponse.ReasonMessage))
            {
                HostPairingProgressReporter.Report(
                    progress,
                    HostConnectionProgressKind.Connection,
                    $"Reason: {connectResponse.ReasonMessage}",
                    source: HostConnectionSource.HostConnection,
                    reasonCode: connectResponse.Reason);
            }

            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Connection,
                $"Reason code: {connectResponse.Reason}",
                source: HostConnectionSource.HostConnection,
                reasonCode: connectResponse.Reason);
            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.Connection,
                $"Accepted: {connectResponse.Accepted}",
                source: HostConnectionSource.HostConnection,
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
                HostConnectionProgressKind.Connection,
                $"Opening WebSocket: {wsUri}",
                source: HostConnectionSource.Transport);

            var connectedSocket = await ConnectWebSocketWithRetryAsync(wsUri, cancellationToken);
            if (connectedSocket is null)
            {
                return PairingConnectionAttempt.FromFailure(
                    "WebSocket endpoint did not become reachable in time.",
                    PairingFailureCodes.WebSocketEndpointUnreachable);
            }

            return PairingConnectionAttempt.FromSuccess(hostAddress!, connectResponse, connectedSocket);
        }

        return lastFailure ?? PairingConnectionAttempt.FromFailure(
            "A current host address is required. Import a fresh pairing config or compact pairing config code.",
            PairingFailureCodes.HostAddressRequired);
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
        int discoveryPort,
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
        await udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(hostAddress, discoveryPort));

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

    private static bool TryResolveHostAddress(string? hostAddressText, out IPAddress? hostAddress)
    {
        hostAddress = null;
        if (string.IsNullOrWhiteSpace(hostAddressText))
        {
            return false;
        }

        return IPAddress.TryParse(hostAddressText.Trim(), out hostAddress);
    }

    private string? ResolveSimulatorLocalHostAddress()
    {
        try
        {
            var address = simulatorLocalHostAddressProvider();
            return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        }
        catch (Exception ex)
        {
            Logger.Info($"Simulator host-address detection failed: {ex.Message}");
            return null;
        }
    }

    private static bool HasSimulatorLocalHostCandidate(string[] candidates, string? simulatorLocalHostAddress)
    {
        if (string.IsNullOrWhiteSpace(simulatorLocalHostAddress))
        {
            return false;
        }

        return candidates.Contains(simulatorLocalHostAddress.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    internal static string BuildHostNetworkCheckMessage(PairingDiscoveryHint? discoveryHint)
    {
        var wifiName = discoveryHint?.WifiName?.Trim();
        if (!string.IsNullOrWhiteSpace(wifiName))
        {
            return $"Check that this device is on the same Wi-Fi network as the Ansight host. Last known host Wi-Fi: {wifiName}.";
        }

        return "Check that this device is on the same Wi-Fi network as the Ansight host.";
    }
}

internal sealed record PairingConnectionAttempt(
    bool Success,
    bool Accepted,
    string Message,
    IPAddress? HostAddress,
    ConnectResponse? ConnectResponse,
    ClientWebSocket? WebSocket,
    string? FailureCode)
{
    public static PairingConnectionAttempt FromFailure(string message, string? failureCode = null)
        => new(false, false, message, null, null, null, failureCode);

    public static PairingConnectionAttempt FromRejected(IPAddress hostAddress, ConnectResponse connectResponse)
        => new(false, false, connectResponse.ReasonMessage ?? connectResponse.Message, hostAddress, connectResponse, null, null);

    public static PairingConnectionAttempt FromSuccess(
        IPAddress hostAddress,
        ConnectResponse connectResponse,
        ClientWebSocket webSocket)
        => new(true, true, "Connected to host and WebSocket session is ready.", hostAddress, connectResponse, webSocket, null);
}
