using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class HostAutoProbeCoordinatorTests
{
    [Fact]
    public async Task OnActivated_UsesCachedProfileUntilConnected()
    {
        var client = new FakeHostAutoProbeSessionClient();
        client.EnqueueConnectResult(HostSessionActionResult.FromSuccess("connected"));

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
        client.EnqueueConnectResult(HostSessionActionResult.FromSuccess("connected"));

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
        client.EnqueueConnectResult(HostSessionActionResult.FromSuccess("connected"));
        client.EnqueueConnectResult(HostSessionActionResult.FromSuccess("reconnected"));

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
        client.EnqueueConnectResult(HostSessionActionResult.FromSuccess("connected"));
        client.EnqueueConnectResult(HostSessionActionResult.FromSuccess("reconnected"));

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
        Assert.Equal(TimeSpan.FromSeconds(1), options.HostAutoProbe.InitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), options.HostAutoProbe.ProbeInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HostAutoProbe.ReconnectDelay);
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
        private readonly Queue<HostSessionActionResult> connectResults = new();

        public int ConnectUsingCachedProfileCallCount { get; private set; }

        public int DisconnectCallCount { get; private set; }

        public bool IsConnected { get; private set; }

        public bool HasCachedProfile { get; set; } = true;

        public DateTimeOffset? LastDisconnectedAtUtc { get; private set; }

        public void EnqueueConnectResult(HostSessionActionResult result)
        {
            connectResults.Enqueue(result);
        }

        public Task<HostSessionActionResult> ConnectUsingCachedProfileAsync(
            string? clientName,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            ConnectUsingCachedProfileCallCount++;
            var result = connectResults.Count > 0
                ? connectResults.Dequeue()
                : HostSessionActionResult.FromFailure("no queued result");
            IsConnected = result.Success;
            if (result.Success)
            {
                LastDisconnectedAtUtc = null;
            }

            return Task.FromResult(result);
        }

        public Task<HostSessionActionResult> DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCallCount++;
            IsConnected = false;
            LastDisconnectedAtUtc = DateTimeOffset.UtcNow;
            return Task.FromResult(HostSessionActionResult.FromSuccess("disconnected"));
        }

        public void MarkDisconnected()
        {
            IsConnected = false;
            LastDisconnectedAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
