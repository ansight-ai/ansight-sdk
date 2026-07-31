using Ansight.Pairing;

namespace Ansight;

internal sealed class HostAutoProbeCoordinator : IDisposable
{
    private readonly HostAutoProbeOptions options;
    private readonly IHostAutoProbeSessionClient autoProbeSessionClient;
    private readonly Lock gate = new();
    private readonly IProgress<HostConnectionProgressUpdate> progress;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private bool disposed;

    public HostAutoProbeCoordinator(
        HostAutoProbeOptions options,
        IHostAutoProbeSessionClient? autoProbeSessionClient = null)
    {
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        this.autoProbeSessionClient = autoProbeSessionClient ?? throw new ArgumentNullException(nameof(autoProbeSessionClient));
        progress = new Progress<HostConnectionProgressUpdate>(HandleProgressUpdate);
    }

    public void OnActivated()
    {
        lock (gate)
        {
            if (disposed || !options.Enabled)
            {
                return;
            }

            if (loopTask is { IsCompleted: false })
            {
                return;
            }

            loopCts = new CancellationTokenSource();
            loopTask = Task.Run(() => RunAsync(loopCts.Token), CancellationToken.None);
        }
    }

    public void OnDeactivated()
    {
        CancellationTokenSource? currentLoopCts;

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            currentLoopCts = loopCts;
            loopCts = null;
            loopTask = null;
        }

        currentLoopCts?.Cancel();
        currentLoopCts?.Dispose();
        _ = autoProbeSessionClient.DisconnectAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        OnDeactivated();
        disposed = true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (options.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.InitialDelay, cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (autoProbeSessionClient.IsConnected)
                {
                    await Task.Delay(options.ProbeInterval, cancellationToken);
                    continue;
                }

                if (autoProbeSessionClient.LastDisconnectedAtUtc is { } lastDisconnectedAtUtc)
                {
                    var remainingReconnectDelay = (lastDisconnectedAtUtc + options.ReconnectDelay) - DateTimeOffset.UtcNow;
                    if (remainingReconnectDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(remainingReconnectDelay, cancellationToken);
                        continue;
                    }
                }

                if (!autoProbeSessionClient.HasCachedProfile
                    && !autoProbeSessionClient.CanAttemptLocalEnrollment)
                {
                    await Task.Delay(options.ProbeInterval, cancellationToken);
                    continue;
                }

                var sessionResult = await autoProbeSessionClient.ConnectAutomaticallyAsync(
                    options.ClientName,
                    progress,
                    cancellationToken);
                if (!sessionResult.Success)
                {
                    await Task.Delay(options.ProbeInterval, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleProgressUpdate(HostConnectionProgressUpdate? update)
    {
        if (update is null || string.IsNullOrWhiteSpace(update.Message))
        {
            return;
        }

        if (update.IsVerbose)
        {
            Logger.Info($"[Ansight Host auto-probe] {update.Message}");
            return;
        }

        Logger.Info($"[Ansight Host auto-probe] {update.Kind}: {update.Message}");
    }
}
