using System.Text.Json;
using System.Threading.Channels;
using Ansight.Network;

namespace Ansight.Pairing;

internal sealed class PairingSessionNetworkStreamer : IDisposable
{
    private const int MaximumPendingRequests = 1000;
    private readonly PairingSessionTransport transport;
    private readonly Lock stateLock = new();
    private NetworkRequestHub? hub;
    private EventHandler<NetworkRequestCapturedEventArgs>? requestCapturedHandler;
    private System.Threading.Channels.Channel<NetworkRequestRecord>? pendingRequests;
    private CancellationTokenSource? pumpCts;
    private Task? pumpTask;
    private bool disposed;

    public PairingSessionNetworkStreamer(PairingSessionTransport transport)
    {
        this.transport = transport;
    }

    public Task<OperationResult> StartAsync(
        NetworkRequestHub hub,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hub);
        if (!transport.IsOpen)
        {
            return Task.FromResult(OperationResult.FromFailure("WebSocket session is not open."));
        }

        StopCore();
        var channel = System.Threading.Channels.Channel.CreateBounded<NetworkRequestRecord>(new BoundedChannelOptions(MaximumPendingRequests)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var cancellation = new CancellationTokenSource();
        EventHandler<NetworkRequestCapturedEventArgs> handler = (_, args) =>
            channel.Writer.TryWrite(args.Request);

        lock (stateLock)
        {
            this.hub = hub;
            requestCapturedHandler = handler;
            pendingRequests = channel;
            pumpCts = cancellation;
            pumpTask = Task.Run(() => RunPumpAsync(channel, cancellation.Token), CancellationToken.None);
            hub.RequestCaptured += handler;
        }

        return Task.FromResult(OperationResult.FromSuccess("Network request streaming started."));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var task = StopCore();
        if (task is null || task.Id == Task.CurrentId)
        {
            return;
        }

        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch
        {
            // Streaming failures must not make session shutdown fail.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopCore();
    }

    private Task? StopCore()
    {
        NetworkRequestHub? currentHub;
        EventHandler<NetworkRequestCapturedEventArgs>? handler;
        System.Threading.Channels.Channel<NetworkRequestRecord>? channel;
        CancellationTokenSource? cancellation;
        Task? task;
        lock (stateLock)
        {
            currentHub = hub;
            handler = requestCapturedHandler;
            channel = pendingRequests;
            cancellation = pumpCts;
            task = pumpTask;
            hub = null;
            requestCapturedHandler = null;
            pendingRequests = null;
            pumpCts = null;
            pumpTask = null;
        }

        if (currentHub is not null && handler is not null)
        {
            currentHub.RequestCaptured -= handler;
        }

        channel?.Writer.TryComplete();
        cancellation?.Cancel();
        cancellation?.Dispose();
        return task;
    }

    private async Task RunPumpAsync(
        System.Threading.Channels.Channel<NetworkRequestRecord> channel,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    type = "CLIENT_NETWORK_REQUEST",
                    sentAtUtc = DateTimeOffset.UtcNow,
                    request
                }, PairingJson.Compact);
                var result = await transport.SendTextAsync(payload, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }
}
