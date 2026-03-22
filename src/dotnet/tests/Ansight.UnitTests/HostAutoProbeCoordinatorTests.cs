using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class HostAutoProbeCoordinatorTests
{
    [Fact]
    public async Task OnActivated_UsesCachedProfileUntilConnected()
    {
        var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueConnectResult(HostConnectionActionResult.FromSuccess("connected"));

        using var coordinator = new HostAutoProbeCoordinator(
            CreateAutoProbeOptions(),
            client);

        coordinator.OnActivated();

        await WaitForAsync(() => client.ConnectUsingCachedProfileCallCount == 1);

        Assert.Equal(1, client.ConnectUsingCachedProfileCallCount);
    }

    [Fact]
    public async Task DoesNotProbeUntilActivated()
    {
        var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueConnectResult(HostConnectionActionResult.FromSuccess("connected"));

        using var coordinator = new HostAutoProbeCoordinator(
            CreateAutoProbeOptions(),
            client);

        await Task.Delay(60);

        Assert.Equal(0, client.ConnectUsingCachedProfileCallCount);
    }

    [Fact]
    public async Task ConnectionLost_RestartsProbeAfterReconnectDelay()
    {
        var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueConnectResult(HostConnectionActionResult.FromSuccess("connected"));
        client.EnqueueConnectResult(HostConnectionActionResult.FromSuccess("reconnected"));

        using var coordinator = new HostAutoProbeCoordinator(
            CreateAutoProbeOptions(reconnectDelayMs: 30),
            client);

        coordinator.OnActivated();
        await WaitForAsync(() => client.ConnectUsingCachedProfileCallCount == 1);

        client.MarkDisconnected();

        await WaitForAsync(() => client.ConnectUsingCachedProfileCallCount >= 2);
        Assert.True(client.ConnectUsingCachedProfileCallCount >= 2);
    }

    [Fact]
    public async Task OnDeactivated_DisconnectsAndStopsFurtherReconnects()
    {
        var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueConnectResult(HostConnectionActionResult.FromSuccess("connected"));
        client.EnqueueConnectResult(HostConnectionActionResult.FromSuccess("reconnected"));

        using var coordinator = new HostAutoProbeCoordinator(
            CreateAutoProbeOptions(reconnectDelayMs: 30),
            client);

        coordinator.OnActivated();
        await WaitForAsync(() => client.ConnectUsingCachedProfileCallCount == 1);

        coordinator.OnDeactivated();
        client.MarkDisconnected();
        await Task.Delay(80);

        Assert.True(client.DisconnectCallCount >= 1);
        Assert.Equal(1, client.ConnectUsingCachedProfileCallCount);
    }

    [Fact]
    public void Options_Default_EnableHostAutoProbe()
    {
        var options = Options.CreateBuilder().Build();

        Assert.True(options.HostAutoProbe.Enabled);
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
        private readonly Queue<HostConnectionActionResult> connectResults = new();

        public int ConnectUsingCachedProfileCallCount { get; private set; }

        public int DisconnectCallCount { get; private set; }

        public bool IsConnected { get; private set; }

        public bool HasCachedProfile { get; set; } = true;

        public DateTimeOffset? LastDisconnectedAtUtc { get; private set; }

        public void EnqueueConnectResult(HostConnectionActionResult result)
        {
            connectResults.Enqueue(result);
        }

        public Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
            string? clientName,
            IProgress<HostPairingProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            ConnectUsingCachedProfileCallCount++;
            var result = connectResults.Count > 0
                ? connectResults.Dequeue()
                : HostConnectionActionResult.FromFailure("no queued result");
            IsConnected = result.Success;
            if (result.Success)
            {
                LastDisconnectedAtUtc = null;
            }

            return Task.FromResult(result);
        }

        public Task<HostConnectionActionResult> DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCallCount++;
            IsConnected = false;
            LastDisconnectedAtUtc = DateTimeOffset.UtcNow;
            return Task.FromResult(HostConnectionActionResult.FromSuccess("disconnected"));
        }

        public void MarkDisconnected()
        {
            IsConnected = false;
            LastDisconnectedAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
