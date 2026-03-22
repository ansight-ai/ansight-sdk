using Ansight.Pairing;

namespace Ansight;

internal sealed class HostAutoProbeCoordinator : IDisposable
{
    private readonly RuntimeImpl runtime;
    private readonly HostAutoProbeOptions options;
    private readonly IHostAutoProbeSessionClient autoProbeSessionClient;
    private readonly Lock gate = new();
    private readonly IProgress<string> progress;
    private CancellationTokenSource? loopCts;
    private Task? loopTask;
    private TaskCompletionSource<bool> sessionClosedSignal = CreateClosedSignal();
    private bool disposed;

    public HostAutoProbeCoordinator(
        RuntimeImpl runtime,
        HostAutoProbeOptions options,
        IHostAutoProbeSessionClient? autoProbeSessionClient = null)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        this.autoProbeSessionClient = autoProbeSessionClient ?? new PairingSessionClient();
        progress = new Progress<string>(HandleProgressMessage);
        this.autoProbeSessionClient.SessionClosed += HandleSessionClosed;
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

            sessionClosedSignal.TrySetResult(true);
            currentLoopCts = loopCts;
            loopCts = null;
            loopTask = null;
        }

        currentLoopCts?.Cancel();
        currentLoopCts?.Dispose();
        _ = autoProbeSessionClient.CloseSessionAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        OnDeactivated();
        disposed = true;
        autoProbeSessionClient.SessionClosed -= HandleSessionClosed;
        autoProbeSessionClient.Dispose();
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
                if (!autoProbeSessionClient.HasCachedPairingProfile)
                {
                    await Task.Delay(options.ProbeInterval, cancellationToken);
                    continue;
                }

                ResetSessionClosedSignal();

                var sessionResult = await autoProbeSessionClient.OpenCachedSessionAsync(
                    options.ClientName,
                    progress,
                    cancellationToken);
                if (!sessionResult.Success)
                {
                    await Task.Delay(options.ProbeInterval, cancellationToken);
                    continue;
                }

                progress.Report("Ansight host session connected.");
                var metricsResult = await autoProbeSessionClient.StartMetricsStreamingAsync(
                    runtime.DataSink,
                    progress,
                    cancellationToken);
                if (!metricsResult.Success)
                {
                    progress.Report($"Metrics stream could not start: {metricsResult.Message}");
                    await autoProbeSessionClient.CloseSessionAsync(CancellationToken.None);
                    await Task.Delay(options.ReconnectDelay, cancellationToken);
                    continue;
                }

                await WaitForSessionClosedAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                progress.Report("Ansight host session closed. Waiting before retry.");
                await Task.Delay(options.ReconnectDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleProgressMessage(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Logger.Info($"[Ansight Host auto-probe] {message}");
        }
    }

    private void HandleSessionClosed(object? sender, EventArgs e)
    {
        lock (gate)
        {
            sessionClosedSignal.TrySetResult(true);
        }
    }

    private void ResetSessionClosedSignal()
    {
        lock (gate)
        {
            sessionClosedSignal = CreateClosedSignal();
        }
    }

    private Task WaitForSessionClosedAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return sessionClosedSignal.Task.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource<bool> CreateClosedSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
