using System.Net;
using Ansight.Input;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.UnitTests;

public sealed class HostConnectionManagerTests
{
    [Fact]
    public async Task ConnectAsync_WhenRuntimeInactive_ReturnsFailureWithoutOpeningSession()
    {
        var runtime = CreateRuntime();
        using var client = new FakeHostConnectionSessionClient();
        using var manager = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, client);

        var result = await manager.ConnectAsync(CreateDocument());

        Assert.False(result.Success);
        Assert.Equal(0, client.OpenSessionCallCount);
        Assert.Equal(HostConnectionState.Disconnected, manager.State);
        Assert.Equal("Activate Ansight before connecting to a host.", manager.StatusSummary);
    }

    [Fact]
    public async Task ConnectAsync_WhenRuntimeActive_OpensSessionAndStartsMetrics()
    {
        var runtime = CreateRuntime();
        runtime.Activate();
        using var client = new FakeHostConnectionSessionClient();
        client.OpenSessionResult = CreateOpenSuccess();
        using var manager = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, client);

        var result = await manager.ConnectAsync(CreateDocument(), clientName: "Unit Test App");

        Assert.True(result.Success);
        Assert.Equal(1, client.OpenSessionCallCount);
        Assert.Equal(1, client.StartMetricsStreamingCallCount);
        Assert.True(manager.IsConnected);
        Assert.Equal(HostConnectionState.Connected, manager.State);
        Assert.Contains("Streaming live metrics to Host at 127.0.0.1.", manager.StatusSummary, StringComparison.Ordinal);

        runtime.Deactivate();
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_OpensANewSession()
    {
        var runtime = CreateRuntime();
        runtime.Activate();
        using var client = new FakeHostConnectionSessionClient();
        client.OpenSessionResult = CreateOpenSuccess();
        using var manager = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, client);

        var initialConnectResult = await manager.ConnectAsync(CreateDocument(), clientName: "Unit Test App");
        var overrideConnectResult = await manager.ConnectAsync(CreateDocument(), clientName: "Unit Test App");

        Assert.True(initialConnectResult.Success);
        Assert.True(overrideConnectResult.Success);
        Assert.Equal(2, client.OpenSessionCallCount);
        Assert.Equal(2, client.StartMetricsStreamingCallCount);

        runtime.Deactivate();
    }

    [Fact]
    public async Task ConnectUsingCachedProfileAsync_WhenAlreadyConnected_ReusesCurrentSession()
    {
        var runtime = CreateRuntime();
        runtime.Activate();
        using var client = new FakeHostConnectionSessionClient();
        client.OpenSessionResult = CreateOpenSuccess();
        client.OpenCachedSessionResult = CreateOpenSuccess();
        using var manager = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, client);

        var initialConnectResult = await manager.ConnectAsync(CreateDocument(), clientName: "Unit Test App");
        var cachedConnectResult = await manager.ConnectUsingCachedProfileAsync("Unit Test App");

        Assert.True(initialConnectResult.Success);
        Assert.True(cachedConnectResult.Success);
        Assert.Equal(1, client.OpenSessionCallCount);
        Assert.Equal(0, client.OpenCachedSessionCallCount);

        runtime.Deactivate();
    }

    [Fact]
    public async Task SessionClosed_UpdatesRetrySummaryWhenAutoProbeIsEnabled()
    {
        var runtime = CreateRuntime();
        runtime.Activate();
        using var client = new FakeHostConnectionSessionClient();
        client.OpenSessionResult = CreateOpenSuccess();
        using var manager = new HostSessionManager(runtime, HostAutoProbeOptions.EnabledDefault, client);

        await manager.ConnectAsync(CreateDocument(), clientName: "Unit Test App");
        client.RaiseSessionClosed();

        Assert.False(manager.IsConnected);
        Assert.Equal(HostConnectionState.Disconnected, manager.State);
        Assert.Contains("Auto-probe will retry", manager.StatusSummary, StringComparison.Ordinal);
        Assert.NotNull(manager.LastDisconnectedAtUtc);

        runtime.Deactivate();
    }

    [Fact]
    public void ClearCachedProfile_ClearsClientCacheAndUpdatesState()
    {
        var runtime = CreateRuntime();
        using var client = new FakeHostConnectionSessionClient();
        using var manager = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, client);

        var result = manager.ClearCachedProfile();

        Assert.True(result.Success);
        Assert.False(client.HasCachedPairingProfile);
        Assert.False(manager.HasCachedProfile);
        Assert.Equal("No Ansight host session is connected.", manager.StatusSummary);
    }

    private static RuntimeImpl CreateRuntime()
    {
        var options = Options.CreateBuilder()
            .WithoutHostAutoProbe()
            .Build();
        return new RuntimeImpl(options);
    }

    private static ParsedPairingDocument CreateDocument()
    {
        return new ParsedPairingDocument
        {
            Config = new PairingConfig
            {
                Schema = "pairing-config/v1",
                ConfigId = "config-1",
                AppId = "com.example.app",
                AppName = "Example App",
                IssuedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                OneTimeToken = "token",
                Host = new PairingHost
                {
                    HostId = "host-1",
                    HostName = "Host",
                    DiscoveryPort = 45123,
                    HostPubKey = "host-pub",
                    HostPubKeyFingerprint = "fingerprint"
                },
                Challenge = new PairingChallenge
                {
                    Alg = "none",
                    ChallengePubKey = "challenge-pub",
                    RequireProofOnFirstPair = false
                },
                Trust = new PairingTrust
                {
                    Mode = "developer",
                    RequireTokenOnFirstPair = false,
                    AllowLanDiscovery = true
                },
                Signature = "signature"
            },
            DiscoveryHint = new PairingDiscoveryHint
            {
                Schema = PairingDiscoveryHint.SchemaName,
                Source = "unit-test",
                HostAddresses = new[] { "127.0.0.1" }
            }
        };
    }

    private static OpenSessionResult CreateOpenSuccess()
    {
        return OpenSessionResult.FromSuccess(
            "connected",
            IPAddress.Loopback,
            new ConnectResponse
            {
                Type = "CONNECT_RESP",
                Ver = 1,
                Accepted = true,
                Reason = "ok",
                HostId = "host-1",
                HostName = "Host",
                Message = "ready",
                WebSocketPort = 45124,
                WebSocketPath = "/ws",
                WebSocketToken = "token"
            });
    }

    private sealed class FakeHostConnectionSessionClient : IHostConnectionSessionClient
    {
        public event EventHandler? SessionClosed;

        public bool IsSessionOpen { get; private set; }

        public bool HasCachedPairingProfile { get; set; } = true;

        public int OpenSessionCallCount { get; private set; }

        public int OpenCachedSessionCallCount { get; private set; }

        public int StartMetricsStreamingCallCount { get; private set; }

        public int StartTouchCaptureStreamingCallCount { get; private set; }

        public OpenSessionResult OpenSessionResult { get; set; } = OpenSessionResult.FromFailure("no session result queued");

        public OpenSessionResult OpenCachedSessionResult { get; set; } = OpenSessionResult.FromFailure("no cached session result queued");

        public OperationResult MetricsStreamingResult { get; set; } = OperationResult.FromSuccess("streaming");

        public bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error)
        {
            document = CreateDocument();
            error = string.Empty;
            return true;
        }

        public Task<OpenSessionResult> OpenSessionAsync(
            ParsedPairingDocument document,
            string clientName,
            PairingConnectionOptions? options,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            OpenSessionCallCount++;
            IsSessionOpen = OpenSessionResult.Success;
            return Task.FromResult(OpenSessionResult);
        }

        public Task<OpenSessionResult> OpenCachedSessionAsync(
            string? clientName,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            OpenCachedSessionCallCount++;
            IsSessionOpen = OpenCachedSessionResult.Success;
            return Task.FromResult(OpenCachedSessionResult);
        }

        public Task<OperationResult> StartMetricsStreamingAsync(
            IDataSink dataSink,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartMetricsStreamingCallCount++;
            if (!MetricsStreamingResult.Success)
            {
                IsSessionOpen = false;
            }

            return Task.FromResult(MetricsStreamingResult);
        }

        public Task<OperationResult> StartTouchCaptureStreamingAsync(
            TouchCaptureHub touchCaptureHub,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartTouchCaptureStreamingCallCount++;
            return Task.FromResult(OperationResult.FromSuccess("touch capture streaming"));
        }

        public Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
        {
            var wasOpen = IsSessionOpen;
            IsSessionOpen = false;
            if (wasOpen)
            {
                SessionClosed?.Invoke(this, EventArgs.Empty);
            }

            return Task.FromResult(OperationResult.FromSuccess("closed"));
        }

        public void ClearCachedPairingProfile()
        {
            HasCachedPairingProfile = false;
        }

        public string ResolveClientName(string? overrideClientName)
        {
            return string.IsNullOrWhiteSpace(overrideClientName)
                ? "Unit Test App"
                : overrideClientName.Trim();
        }

        public void RaiseSessionClosed()
        {
            IsSessionOpen = false;
            SessionClosed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }
}
