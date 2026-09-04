using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.UnitTests;

public sealed class PairingSessionConnectorTests
{
    [Fact]
    public async Task ConnectAsync_WhenWifiIsUnavailable_ReturnsSpecificFailure()
    {
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.NotConnected);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: IPAddress.Loopback.ToString(),
                wifiName: "Office Wifi")
        };

        var result = await connector.ConnectAsync(
            document,
            "Unit Test App",
            null,
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(
            "Ansight is unavailable because this device is not connected to Wi-Fi. Check that this device is on the same Wi-Fi network as the Ansight host. Last known host Wi-Fi: Office Wifi.",
            result.Message);
        Assert.Equal(PairingFailureCodes.WifiRequired, result.FailureCode);
        Assert.Null(result.HostAddress);
        Assert.Null(result.ConnectResponse);
        Assert.Null(result.WebSocket);
    }

    [Fact]
    public async Task ConnectAsync_WhenCellularConnectionsAreDisabled_ReturnsSpecificFailure()
    {
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Cellular);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: IPAddress.Loopback.ToString())
        };

        var result = await connector.ConnectAsync(
            document,
            "Unit Test App",
            options: null,
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(
            "Cellular host connections are disabled. Enable AllowCellularConnections in the Ansight host connection builder to connect while using cellular data.",
            result.Message);
        Assert.Equal(PairingFailureCodes.WifiRequired, result.FailureCode);
        Assert.Null(result.HostAddress);
        Assert.Null(result.ConnectResponse);
        Assert.Null(result.WebSocket);
    }

    [Fact]
    public void ResolveCandidates_WhenSimulatorLocalHostIsAvailable_PrefersItOverDiscoveryAddresses()
    {
        var candidates = PairingDiscoveryHintHostAddresses.ResolveCandidates(
            PairingTestDocumentFactory.CreateDiscoveryHint(hostAddresses: ["192.0.2.10", "127.0.0.1"]),
            hostAddressOverride: null,
            simulatorLocalHostAddress: "127.0.0.1");

        Assert.Equal(["127.0.0.1", "192.0.2.10"], candidates);
    }

    [Fact]
    public void ResolveCandidates_WhenHostOverrideIsProvided_DoesNotAppendSimulatorLocalHost()
    {
        var candidates = PairingDiscoveryHintHostAddresses.ResolveCandidates(
            PairingTestDocumentFactory.CreateDiscoveryHint(hostAddresses: ["192.0.2.10"]),
            hostAddressOverride: "10.0.0.20",
            simulatorLocalHostAddress: "127.0.0.1");

        Assert.Equal(["10.0.0.20"], candidates);
    }

    [Fact]
    public async Task ConnectAsync_WhenWifiUnavailableButSimulatorFallbackExists_UsesSimulatorLocalHost()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(
            () => PairingWifiPreflightStatus.NotConnected,
            () => IPAddress.Loopback.ToString());
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: listenerEndPoint.Port);
        var document = new ParsedPairingDocument
        {
            Config = config
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            options: null,
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal(config.ConfigId, parsedRequest!.InviteId);
        Assert.Equal(PairingEnrollmentModes.Invite, parsedRequest.EnrollmentMode);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(IPAddress.Loopback, result.HostAddress);
    }

    [Fact]
    public async Task ConnectAsync_WhenRuntimeLocalDocumentIsUsed_SendsLocalEnrollmentMode()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(
            () => PairingWifiPreflightStatus.NotConnected,
            () => IPAddress.Loopback.ToString());
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: listenerEndPoint.Port);
        config.ConfigId = $"{PairingEnrollmentModes.LocalConfigPrefix}{config.AppId}";
        var document = new ParsedPairingDocument
        {
            Config = config
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            options: null,
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal(PairingEnrollmentModes.Local, parsedRequest!.EnrollmentMode);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConnectAsync_WhenGenericInviteIsUsed_SendsRuntimeAppId()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(
            appId: PairingConfig.AnyAppId,
            appName: "Any Ansight app",
            discoveryPort: listenerEndPoint.Port);
        var document = new ParsedPairingDocument
        {
            Config = config,
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: IPAddress.Loopback.ToString(),
                discoveryPort: listenerEndPoint.Port)
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            new PairingConnectionOptions
            {
                DeviceAppProfile = new DeviceAppProfile
                {
                    App = new DeviceApplicationProfile
                    {
                        AppId = "com.example.runtime-app"
                    }
                }
            },
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal("com.example.runtime-app", parsedRequest!.AppId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConnectAsync_WhenCellularConnectionsAreEnabled_AttemptsTheHostConnection()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Cellular);
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: listenerEndPoint.Port);
        var document = new ParsedPairingDocument
        {
            Config = config,
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: IPAddress.Loopback.ToString(),
                discoveryPort: listenerEndPoint.Port)
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            new PairingConnectionOptions
            {
                AllowCellularConnections = true
            },
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal(config.ConfigId, parsedRequest!.InviteId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(IPAddress.Loopback, result.HostAddress);
    }

    [Fact]
    public void BuildHostNetworkCheckMessage_WhenWifiNameIsUnavailable_UsesGenericSameWifiMessage()
    {
        var message = PairingSessionConnector.BuildHostNetworkCheckMessage(
            PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: IPAddress.Loopback.ToString(),
                wifiName: " "));

        Assert.Equal(
            "Check that this device is on the same Wi-Fi network as the Ansight host.",
            message);
    }

    [Fact]
    public async Task SendConnectRequestAsync_IncludesStableProcessSessionId()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;

        var config = PairingTestDocumentFactory.CreateEnrollmentInvite();

        var method = typeof(PairingSessionConnector).GetMethod(
            "SendConnectRequestAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var responseTask = (Task<ConnectResponse?>)method!.Invoke(
            obj: null,
            parameters:
            [
                config,
                config.AppId,
                "Unit Test App",
                IPAddress.Loopback,
                listenerEndPoint.Port,
                CancellationToken.None
            ])!;

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.False(string.IsNullOrWhiteSpace(parsedRequest!.ProcessSessionId));
        Assert.Equal(ProcessSessionIdentity.Current, parsedRequest.ProcessSessionId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = true,
                Reason = "Ok",
                HostId = "host-1",
                HostName = "Host",
                HostWifiName = "Office Wifi",
                Message = "Accepted"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var response = await responseTask;
        Assert.NotNull(response);
        Assert.True(response!.Accepted);
        Assert.Equal("Office Wifi", response.HostWifiName);
    }

    [Fact]
    public async Task ConnectAsync_WhenDiscoveryPortOverrideIsProvided_UsesItForUdpBootstrap()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: 41000);
        var document = new ParsedPairingDocument
        {
            Config = config,
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: IPAddress.Loopback.ToString(), discoveryPort: 41000)
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            new PairingConnectionOptions
            {
                DiscoveryPort = listenerEndPoint.Port
            },
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal(config.ConfigId, parsedRequest!.InviteId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(IPAddress.Loopback, result.HostAddress);
    }

    [Fact]
    public async Task ConnectAsync_WhenDiscoveryHintPortIsMissing_UsesConfigHostDiscoveryPort()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: listenerEndPoint.Port);
        var document = new ParsedPairingDocument
        {
            Config = config,
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: IPAddress.Loopback.ToString(), discoveryPort: null)
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            options: null,
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal(config.ConfigId, parsedRequest!.InviteId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(IPAddress.Loopback, result.HostAddress);
    }

    [Fact]
    public async Task ConnectAsync_WhenDiscoveryHintHasMultipleAddresses_TriesNextValidCandidate()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: listenerEndPoint.Port);
        var document = new ParsedPairingDocument
        {
            Config = config,
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddresses: ["not-an-ip", IPAddress.Loopback.ToString()],
                discoveryPort: listenerEndPoint.Port)
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            options: null,
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.Equal(config.ConfigId, parsedRequest!.InviteId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest.RequestId,
                Accepted = false,
                Reason = "pairing-required",
                ReasonMessage = "Need WebSocket handoff",
                HostId = "host-1",
                HostName = "Host",
                Message = "Rejected"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;
        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal(IPAddress.Loopback, result.HostAddress);
    }

    [Fact]
    public async Task ConnectAsync_WhenHostRequiresSignIn_SurfacesSignInRejection()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateEnrollmentInvite(discoveryPort: listenerEndPoint.Port);
        var document = new ParsedPairingDocument
        {
            Config = config,
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: IPAddress.Loopback.ToString(), discoveryPort: listenerEndPoint.Port)
        };

        var connectTask = connector.ConnectAsync(
            document,
            "Unit Test App",
            options: null,
            progress: null,
            CancellationToken.None);

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(
            request.Buffer,
            PairingJson.Compact);
        Assert.NotNull(parsedRequest);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = parsedRequest!.RequestId,
                Accepted = false,
                Reason = PairingFailureCodes.SignInRequired,
                ReasonMessage = "Sign in required. Sign in to Ansight host before connecting an app.",
                HostId = "host-1",
                HostName = "Host",
                Message = "Sign in required. Sign in to Ansight host before connecting an app."
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var result = await connectTask;

        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal("Sign in required. Sign in to Ansight host before connecting an app.", result.Message);
        Assert.Equal(IPAddress.Loopback, result.HostAddress);
        Assert.NotNull(result.ConnectResponse);
        Assert.Equal(PairingFailureCodes.SignInRequired, result.ConnectResponse!.Reason);
        Assert.Equal("Sign in required. Sign in to Ansight host before connecting an app.", result.ConnectResponse.ReasonMessage);
    }
}
