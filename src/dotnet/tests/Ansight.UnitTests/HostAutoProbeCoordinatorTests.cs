using System.Net;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class HostAutoProbeCoordinatorTests
{
    [Fact]
    public async Task OnActivated_UsesCachedProfileAndStartsTelemetry()
    {
        var runtime = CreateRuntime();
        using var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueOpenResult(CreateOpenSuccess());
        client.MetricsStartResult = OperationResult.FromSuccess("metrics");

        using var coordinator = new HostAutoProbeCoordinator(
            runtime,
            CreateAutoProbeOptions(),
            client);

        coordinator.OnActivated();

        await WaitForAsync(() => client.OpenCachedSessionCallCount == 1 && client.StartMetricsStreamingCallCount == 1);

        Assert.Equal(1, client.OpenCachedSessionCallCount);
        Assert.Equal(1, client.StartMetricsStreamingCallCount);
    }

    [Fact]
    public async Task DoesNotProbeUntilActivated()
    {
        var runtime = CreateRuntime();
        using var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueOpenResult(CreateOpenSuccess());

        using var coordinator = new HostAutoProbeCoordinator(
            runtime,
            CreateAutoProbeOptions(),
            client);

        await Task.Delay(60);

        Assert.Equal(0, client.OpenCachedSessionCallCount);
        Assert.Equal(0, client.StartMetricsStreamingCallCount);
    }

    [Fact]
    public async Task SessionClosed_RestartsProbeAfterReconnectDelay()
    {
        var runtime = CreateRuntime();
        using var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueOpenResult(CreateOpenSuccess());
        client.EnqueueOpenResult(CreateOpenSuccess());
        client.MetricsStartResult = OperationResult.FromSuccess("metrics");

        using var coordinator = new HostAutoProbeCoordinator(
            runtime,
            CreateAutoProbeOptions(reconnectDelayMs: 30),
            client);

        coordinator.OnActivated();
        await WaitForAsync(() => client.OpenCachedSessionCallCount == 1 && client.StartMetricsStreamingCallCount == 1);

        client.RaiseSessionClosed();

        await WaitForAsync(() => client.OpenCachedSessionCallCount >= 2);
        Assert.True(client.OpenCachedSessionCallCount >= 2);
    }

    [Fact]
    public async Task OnDeactivated_ClosesCurrentSessionAndStopsFurtherReconnects()
    {
        var runtime = CreateRuntime();
        using var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueOpenResult(CreateOpenSuccess());
        client.EnqueueOpenResult(CreateOpenSuccess());
        client.MetricsStartResult = OperationResult.FromSuccess("metrics");

        using var coordinator = new HostAutoProbeCoordinator(
            runtime,
            CreateAutoProbeOptions(reconnectDelayMs: 30),
            client);

        coordinator.OnActivated();
        await WaitForAsync(() => client.OpenCachedSessionCallCount == 1 && client.StartMetricsStreamingCallCount == 1);

        coordinator.OnDeactivated();
        client.RaiseSessionClosed();
        await Task.Delay(80);

        Assert.True(client.CloseSessionCallCount >= 1);
        Assert.Equal(1, client.OpenCachedSessionCallCount);
    }

    [Fact]
    public void Options_Default_EnableHostAutoProbe()
    {
        var options = Options.CreateBuilder().Build();

        Assert.True(options.HostAutoProbe.Enabled);
    }

    private static RuntimeImpl CreateRuntime()
    {
        var options = Options.CreateBuilder()
            .WithoutHostAutoProbe()
            .Build();
        return new RuntimeImpl(options);
    }

    private static HostAutoProbeOptions CreateAutoProbeOptions(
        int initialDelayMs = 0,
        int probeDelayMs = 20,
        int reconnectDelayMs = 20)
    {
        return new HostAutoProbeOptions
        {
            Enabled = true,
            InitialDelay = TimeSpan.FromMilliseconds(initialDelayMs),
            ProbeInterval = TimeSpan.FromMilliseconds(probeDelayMs),
            ReconnectDelay = TimeSpan.FromMilliseconds(reconnectDelayMs),
            ClientName = "Unit Test App"
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
                HostName = "Studio",
                Message = "ready",
                WebSocketPort = 45124,
                WebSocketPath = "/ws",
                WebSocketToken = "token"
            },
            "HOST_HELLO");
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 1000)
    {
        var started = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not satisfied before the timeout elapsed.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FakeHostAutoProbeSessionClient : IHostAutoProbeSessionClient
    {
        private readonly Queue<OpenSessionResult> openResults = new();

        public int OpenCachedSessionCallCount { get; private set; }

        public int StartMetricsStreamingCallCount { get; private set; }

        public int CloseSessionCallCount { get; private set; }

        public bool IsSessionOpen { get; private set; }

        public bool HasCachedPairingProfile { get; set; } = true;

        public OperationResult MetricsStartResult { get; set; } = OperationResult.FromSuccess("metrics");

        public event EventHandler? SessionClosed;

        public void EnqueueOpenResult(OpenSessionResult result)
        {
            openResults.Enqueue(result);
        }

        public Task<OpenSessionResult> OpenCachedSessionAsync(
            string? clientName,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            OpenCachedSessionCallCount++;
            var result = openResults.Count > 0
                ? openResults.Dequeue()
                : OpenSessionResult.FromFailure("no queued result");
            IsSessionOpen = result.Success;
            return Task.FromResult(result);
        }

        public Task<OperationResult> StartMetricsStreamingAsync(
            IDataSink dataSink,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            StartMetricsStreamingCallCount++;
            if (!MetricsStartResult.Success)
            {
                IsSessionOpen = false;
            }

            return Task.FromResult(MetricsStartResult);
        }

        public Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
        {
            CloseSessionCallCount++;
            if (IsSessionOpen)
            {
                IsSessionOpen = false;
                SessionClosed?.Invoke(this, EventArgs.Empty);
            }

            return Task.FromResult(OperationResult.FromSuccess("closed"));
        }

        public void ClearCachedPairingProfile()
        {
            HasCachedPairingProfile = false;
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
