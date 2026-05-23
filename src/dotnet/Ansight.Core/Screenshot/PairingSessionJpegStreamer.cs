using Ansight.Pairing;

namespace Ansight.Screenshot;

internal sealed class PairingSessionJpegStreamer : IDisposable
{
    private readonly PairingSessionTransport transport;
    private CancellationTokenSource? captureCts;
    private Task? captureTask;
    private bool disposed;

    public PairingSessionJpegStreamer(PairingSessionTransport transport)
    {
        this.transport = transport;
    }

    public async Task StartAsync(IProgress<HostConnectionProgressUpdate>? progress)
    {
        await StopAsync(CancellationToken.None);

        var options = ResolveOptions();
        if (options is null)
        {
            return;
        }

        captureCts = new CancellationTokenSource();
        captureTask = Task.Run(() => RunCapturePumpAsync(options, progress, captureCts.Token));
        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.SessionJpegCapture,
            $"Session JPEG capture started ({options.IntervalMilliseconds}ms, quality {options.Quality}, max width {(options.MaxWidth?.ToString() ?? "native")}).",
            source: HostConnectionSource.SessionJpegCapture);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var captureCts = this.captureCts;
        var captureTask = this.captureTask;

        this.captureCts = null;
        this.captureTask = null;

        captureCts?.Cancel();

        if (captureTask is not null)
        {
            try
            {
                await captureTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch
            {
                // Ignore pump errors during shutdown.
            }
        }

        captureCts?.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        captureCts?.Cancel();
        captureCts?.Dispose();
        captureTask = null;
        captureCts = null;
    }

    private static SessionJpegCaptureOptions? ResolveOptions()
    {
        if (!Runtime.IsInitialized)
        {
            return null;
        }

        var configured = Runtime.MutableInstance.Options.SessionJpegCapture;
        if (configured is null)
        {
            return null;
        }

        return new SessionJpegCaptureOptions
        {
            IntervalMilliseconds = configured.IntervalMilliseconds,
            Quality = configured.Quality,
            MaxWidth = configured.MaxWidth
        };
    }

    private static bool ShouldSkipCaptureForLifecycle()
    {
        return Runtime.CurrentAppLifecycleState == AppLifecycleState.Background;
    }

    private async Task RunCapturePumpAsync(
        SessionJpegCaptureOptions options,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(options.IntervalMilliseconds);
        var captureImmediately = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!captureImmediately)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            captureImmediately = false;

            try
            {
                if (ShouldSkipCaptureForLifecycle())
                {
                    continue;
                }

                var surface = await SessionJpegCaptureSupport.CaptureSurfaceAsync(options, cancellationToken);
                if (surface is null)
                {
                    continue;
                }

                using (surface)
                {
                    var sendResult = await SessionJpegCaptureSupport.SendSurfaceAsync(
                        surface,
                        options,
                        transport,
                        cancellationToken);
                    if (!sendResult.Success)
                    {
                        HostPairingProgressReporter.Report(
                            progress,
                            HostConnectionProgressKind.Warning,
                            $"Session JPEG capture stopped: {sendResult.Message}",
                            source: HostConnectionSource.SessionJpegCapture);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Session JPEG capture skipped: {ex.Message}");
            }
        }
    }
}
