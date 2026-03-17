using System.Diagnostics;
using System.Net.WebSockets;

namespace Ansight.Pairing;

internal sealed class PairingSessionJpegStreamer : IDisposable
{
    private readonly PairingSessionTransport _transport;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _disposed;

    public PairingSessionJpegStreamer(PairingSessionTransport transport)
    {
        _transport = transport;
    }

    public async Task StartAsync(IProgress<string>? progress)
    {
        await StopAsync(CancellationToken.None);

        var options = ResolveOptions();
        if (options is null)
        {
            return;
        }

        _captureCts = new CancellationTokenSource();
        _captureTask = Task.Run(() => RunCapturePumpAsync(options, progress, _captureCts.Token));
        progress?.Report(
            $"Session JPEG capture started ({options.IntervalMilliseconds}ms, quality {options.Quality}, max width {(options.MaxWidth?.ToString() ?? "native")}).");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var captureCts = _captureCts;
        var captureTask = _captureTask;

        _captureCts = null;
        _captureTask = null;

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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _captureTask = null;
        _captureCts = null;
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
        IProgress<string>? progress,
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
        IProgress<string>? progress,
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

                    var sendResult = await _transport.SendBinaryAsync(frame.Payload, WebSocketMessageType.Binary, cancellationToken);
                    if (!sendResult.Success)
                    {
                        progress?.Report($"Session JPEG capture stopped: {sendResult.Message}");
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
        private readonly Lock _gate = new();
        private readonly SemaphoreSlim _signal = new(0);
        private ISessionJpegCaptureSurface? _pendingSurface;
        private bool _hasSignal;
        private bool _completed;

        public void Enqueue(ISessionJpegCaptureSurface surface)
        {
            ArgumentNullException.ThrowIfNull(surface);

            var releaseSignal = false;
            ISessionJpegCaptureSurface? displacedSurface = null;
            lock (_gate)
            {
                if (_completed)
                {
                    displacedSurface = surface;
                }
                else
                {
                    displacedSurface = _pendingSurface;
                    _pendingSurface = surface;
                    if (!_hasSignal)
                    {
                        _hasSignal = true;
                        releaseSignal = true;
                    }
                }
            }

            displacedSurface?.Dispose();

            if (releaseSignal)
            {
                _signal.Release();
            }
        }

        public async Task<ISessionJpegCaptureSurface?> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);

            lock (_gate)
            {
                _hasSignal = false;
                var surface = _pendingSurface;
                _pendingSurface = null;
                return surface;
            }
        }

        public void Complete()
        {
            var releaseSignal = false;
            ISessionJpegCaptureSurface? displacedSurface = null;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                displacedSurface = _pendingSurface;
                _pendingSurface = null;
                if (!_hasSignal)
                {
                    _hasSignal = true;
                    releaseSignal = true;
                }
            }

            displacedSurface?.Dispose();

            if (releaseSignal)
            {
                _signal.Release();
            }
        }

        public void Dispose()
        {
            Complete();
            _signal.Dispose();
        }
    }
}
