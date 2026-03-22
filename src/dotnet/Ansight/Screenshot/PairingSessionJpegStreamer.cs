using System.Diagnostics;
using System.Net.WebSockets;
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

    public async Task StartAsync(IProgress<HostPairingProgressUpdate>? progress)
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
            HostPairingProgressKind.SessionJpegCapture,
            $"Session JPEG capture started ({options.IntervalMilliseconds}ms, quality {options.Quality}, max width {(options.MaxWidth?.ToString() ?? "native")}).",
            source: HostPairingSource.SessionJpegCapture);
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

    private async Task RunCapturePumpAsync(
        SessionJpegCaptureOptions options,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var pendingSurfaces = new LatestSessionJpegSurfaceQueue();
        var workerTask = Task.Run(
            () => RunEncodePumpAsync(options, pendingSurfaces, progress, linkedCancellation, linkedCancellation.Token),
            linkedCancellation.Token);

        var interval = TimeSpan.FromMilliseconds(options.IntervalMilliseconds);
        var nextCaptureAt = Stopwatch.GetTimestamp();

        try
        {
            while (!linkedCancellation.Token.IsCancellationRequested)
            {
                var remainingDelay = nextCaptureAt - Stopwatch.GetTimestamp();
                if (remainingDelay > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(remainingDelay / (double)Stopwatch.Frequency), linkedCancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                try
                {
                    var surface = await SessionJpegCaptureSupport.CaptureSurfaceAsync(options, linkedCancellation.Token);
                    if (surface is not null)
                    {
                        pendingSurfaces.Enqueue(surface);
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

                nextCaptureAt += (long)(interval.TotalSeconds * Stopwatch.Frequency);
                var now = Stopwatch.GetTimestamp();
                if (nextCaptureAt < now - (long)(interval.TotalSeconds * Stopwatch.Frequency))
                {
                    nextCaptureAt = now;
                }
            }
        }
        finally
        {
            pendingSurfaces.Complete();
            linkedCancellation.Cancel();

            try
            {
                await workerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RunEncodePumpAsync(
        SessionJpegCaptureOptions options,
        LatestSessionJpegSurfaceQueue pendingSurfaces,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationTokenSource linkedCancellation,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ISessionJpegCaptureSurface? surface;
            try
            {
                surface = await pendingSurfaces.DequeueAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (surface is null)
            {
                break;
            }

            using (surface)
            {
                try
                {
                    using var frame = SessionJpegCaptureSupport.EncodeSurface(surface, options);
                    if (frame is null)
                    {
                        continue;
                    }

                    var sendResult = await transport.SendBinaryAsync(frame.Payload, WebSocketMessageType.Binary, cancellationToken);
                    if (!sendResult.Success)
                    {
                        HostPairingProgressReporter.Report(
                            progress,
                            HostPairingProgressKind.Warning,
                            $"Session JPEG capture stopped: {sendResult.Message}",
                            source: HostPairingSource.SessionJpegCapture);
                        linkedCancellation.Cancel();
                        return;
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

    private sealed class LatestSessionJpegSurfaceQueue : IDisposable
    {
        private readonly Lock gate = new();
        private readonly SemaphoreSlim signal = new(0);
        private ISessionJpegCaptureSurface? pendingSurface;
        private bool hasSignal;
        private bool completed;

        public void Enqueue(ISessionJpegCaptureSurface surface)
        {
            ArgumentNullException.ThrowIfNull(surface);

            var releaseSignal = false;
            ISessionJpegCaptureSurface? displacedSurface = null;
            lock (gate)
            {
                if (completed)
                {
                    displacedSurface = surface;
                }
                else
                {
                    displacedSurface = pendingSurface;
                    pendingSurface = surface;
                    if (!hasSignal)
                    {
                        hasSignal = true;
                        releaseSignal = true;
                    }
                }
            }

            displacedSurface?.Dispose();

            if (releaseSignal)
            {
                signal.Release();
            }
        }

        public async Task<ISessionJpegCaptureSurface?> DequeueAsync(CancellationToken cancellationToken)
        {
            await signal.WaitAsync(cancellationToken);

            lock (gate)
            {
                hasSignal = false;
                var surface = pendingSurface;
                pendingSurface = null;
                return surface;
            }
        }

        public void Complete()
        {
            var releaseSignal = false;
            ISessionJpegCaptureSurface? displacedSurface = null;
            lock (gate)
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                displacedSurface = pendingSurface;
                pendingSurface = null;
                if (!hasSignal)
                {
                    hasSignal = true;
                    releaseSignal = true;
                }
            }

            displacedSurface?.Dispose();

            if (releaseSignal)
            {
                signal.Release();
            }
        }

        public void Dispose()
        {
            Complete();
            signal.Dispose();
        }
    }
}
