using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.UnitTests;

public sealed class PairingSessionConnectorTests
{
    [Fact]
    public async Task ConnectAsync_WhenWifiIsUnavailable_ReturnsSpecificFailure()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.NotConnected);
        var document = new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(signingKey),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: IPAddress.Loopback.ToString())
        };

        var result = await connector.ConnectAsync(
            document,
            "Unit Test App",
            null,
            progress: null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Accepted);
        Assert.Equal("Ansight is unavailable because this device is not connected to Wi-Fi.", result.Message);
        Assert.Equal(PairingFailureCodes.WifiRequired, result.FailureCode);
        Assert.Null(result.HostAddress);
        Assert.Null(result.ConnectResponse);
        Assert.Null(result.WebSocket);
    }

    [Fact]
    public async Task SendConnectRequestAsync_IncludesStableProcessSessionId()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;

        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey);

        var method = typeof(PairingSessionConnector).GetMethod(
            "SendConnectRequestAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var responseTask = (Task<ConnectResponse?>)method!.Invoke(
            obj: null,
            parameters:
            [
                config,
                "Unit Test App",
                IPAddress.Loopback,
                listenerEndPoint.Port,
                false,
                CancellationToken.None
            ])!;

        var request = await listener.ReceiveAsync();
        var parsedRequest = JsonSerializer.Deserialize<ConnectRequest>(request.Buffer, PairingJson.Compact);

        Assert.NotNull(parsedRequest);
        Assert.False(string.IsNullOrWhiteSpace(parsedRequest!.ProcessSessionId));
        Assert.Equal(ProcessSessionIdentity.Current, parsedRequest.ProcessSessionId);
        Assert.False(parsedRequest.DevelopmentPairing);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "CONNECT_RESP",
                Ver = 1,
                Accepted = true,
                Reason = "Ok",
                HostId = "host-1",
                HostName = "Host",
                Message = "Accepted"
            },
            PairingJson.Compact);
        await listener.SendAsync(payload, payload.Length, request.RemoteEndPoint);

        var response = await responseTask;
        Assert.NotNull(response);
        Assert.True(response!.Accepted);
    }

    [Fact]
    public async Task ConnectAsync_WhenDevelopmentPairingIsEnabled_SendsDevelopmentPairingFlag()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var document = new ParsedPairingDocument
        {
            Config = new PairingConfig
            {
                Schema = "ansight.development-pairing-config.v1",
                ConfigId = "ansight-development-pairing",
                AppId = "com.ansight.dev",
                AppName = "Ansight Dev",
                IssuedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.MaxValue,
                OneTimeToken = string.Empty,
                Host = new PairingHost
                {
                    DiscoveryPort = listenerEndPoint.Port,
                    HostPubKey = string.Empty,
                    HostPubKeyFingerprint = string.Empty
                },
                Challenge = new PairingChallenge
                {
                    Alg = "none",
                    ChallengePubKey = string.Empty,
                    RequireProofOnFirstPair = false
                },
                Trust = new PairingTrust
                {
                    Mode = "development-auto",
                    RequireTokenOnFirstPair = false,
                    AllowLanDiscovery = true
                },
                Signature = string.Empty
            },
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: IPAddress.Loopback.ToString(),
                discoveryPort: listenerEndPoint.Port),
            IsDevelopmentPairing = true
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
        Assert.True(parsedRequest!.DevelopmentPairing);
        Assert.Equal("ansight-development-pairing", parsedRequest.ConfigId);
        Assert.Equal(string.Empty, parsedRequest.OneTimeToken);
        Assert.Equal("com.ansight.dev", parsedRequest.AppId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "CONNECT_RESP",
                Ver = 1,
                Accepted = false,
                Reason = "development-test",
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
    public async Task ConnectAsync_WhenDiscoveryPortOverrideIsProvided_UsesItForUdpBootstrap()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, discoveryPort: 41000);
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
        Assert.Equal(config.ConfigId, parsedRequest!.ConfigId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "CONNECT_RESP",
                Ver = 1,
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
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var listenerEndPoint = (IPEndPoint)listener.Client.LocalEndPoint!;
        var connector = new PairingSessionConnector(() => PairingWifiPreflightStatus.Connected);
        var config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, discoveryPort: listenerEndPoint.Port);
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
        Assert.Equal(config.ConfigId, parsedRequest!.ConfigId);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ConnectResponse
            {
                Type = "CONNECT_RESP",
                Ver = 1,
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
}
