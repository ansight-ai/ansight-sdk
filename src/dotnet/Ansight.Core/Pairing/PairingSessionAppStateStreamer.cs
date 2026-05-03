using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ansight.Pairing;

internal sealed class PairingSessionAppStateStreamer : IDisposable
{
    private readonly PairingSessionTransport transport;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Lock stateLock = new();
    private EventHandler<AppLifecycleStateChangedEventArgs>? appLifecycleStateChangedHandler;
    private AppLifecycleState? lastSentAppLifecycleState;
    private bool started;
    private bool disposed;

    public PairingSessionAppStateStreamer(PairingSessionTransport transport)
    {
        this.transport = transport;
    }

    public async Task<OperationResult> StartAsync(
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (!transport.IsOpen)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        await StopAsync(CancellationToken.None);

        lock (stateLock)
        {
            appLifecycleStateChangedHandler = HandleAppLifecycleStateChanged;
            lastSentAppLifecycleState = null;
            started = true;
            Runtime.AppLifecycleStateChanged += appLifecycleStateChangedHandler;
        }

        var result = await SendCurrentStateAsync(progress, cancellationToken);
        if (result.Success)
        {
            return result;
        }

        await StopAsync(CancellationToken.None);
        return result;
    }

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken)
    {
        EventHandler<AppLifecycleStateChangedEventArgs>? handler;

        lock (stateLock)
        {
            if (!started)
            {
                return Task.FromResult(OperationResult.FromSuccess("App state streaming already stopped."));
            }

            started = false;
            handler = appLifecycleStateChangedHandler;
            appLifecycleStateChangedHandler = null;
            lastSentAppLifecycleState = null;
        }

        if (handler is not null)
        {
            Runtime.AppLifecycleStateChanged -= handler;
        }

        return Task.FromResult(OperationResult.FromSuccess("App state streaming stopped."));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        _ = StopAsync(CancellationToken.None);
        sendLock.Dispose();
    }

    private void HandleAppLifecycleStateChanged(object? sender, AppLifecycleStateChangedEventArgs args)
    {
        _ = SendAppStateIfNeededAsync(args.State, args.ChangedAtUtc, progress: null, CancellationToken.None);
    }

    private Task<OperationResult> SendCurrentStateAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        return SendAppStateIfNeededAsync(
            Runtime.CurrentAppLifecycleState,
            Runtime.CurrentAppLifecycleStateChangedUtc,
            progress,
            cancellationToken);
    }

    private async Task<OperationResult> SendAppStateIfNeededAsync(
        AppLifecycleState state,
        DateTimeOffset? changedAtUtc,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        bool isStarted;
        lock (stateLock)
        {
            isStarted = started;
        }

        if (!isStarted)
        {
            return OperationResult.FromSuccess("App state streaming is not running.");
        }

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            AppLifecycleState? previouslySentState;
            lock (stateLock)
            {
                if (!started)
                {
                    return OperationResult.FromSuccess("App state streaming is not running.");
                }

                previouslySentState = lastSentAppLifecycleState;
            }

            if (previouslySentState == state)
            {
                return OperationResult.FromSuccess("App state already sent.");
            }

            var payload = new JsonObject
            {
                ["state"] = SerializeState(state),
                ["changedAtUtc"] = changedAtUtc?.ToUniversalTime()
            };

            var result = await transport.SendControlRequestAsync(
                PairingControlActions.AppState,
                payload,
                "WS -> app.state",
                "App state sent.",
                "Failed to send app state",
                progress,
                TimeSpan.FromSeconds(15),
                cancellationToken,
                HostConnectionSource.AppState,
                HostConnectionProgressKind.AppState);
            if (result.Success)
            {
                lock (stateLock)
                {
                    lastSentAppLifecycleState = state;
                }
            }

            return result;
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static string SerializeState(AppLifecycleState state)
    {
        return state switch
        {
            AppLifecycleState.Foreground => "foreground",
            AppLifecycleState.Background => "background",
            _ => "unknown"
        };
    }
}
