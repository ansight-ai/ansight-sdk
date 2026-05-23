using System.Text.Json;
using Ansight.Input;

namespace Ansight.Pairing;

internal sealed class PairingSessionTouchCaptureStreamer : IDisposable
{
    private const int MaxBatchSize = 200;
    private const int MaxPendingTouches = 2000;
    private const string TouchInputType = "CLIENT_TOUCH_INPUT";
    private const string TouchSchemaName = "ansight.touches.v1";
    private const string WindowSpaceCode = "w";
    private const string PixelUnitCode = "px";
    private const string PointUnitCode = "pt";
    private const int ActionDown = 0;
    private const int ActionMove = 1;
    private const int ActionUp = 2;
    private const int ActionCancel = 3;
    private const int ActionUnknown = 4;

    private readonly PairingSessionTransport transport;
    private readonly SemaphoreSlim signal = new(0);
    private readonly Lock touchLock = new();
    private readonly List<CapturedTouch> pendingTouches = [];
    private TouchCaptureHub? touchCaptureHub;
    private EventHandler<TouchCapturedEventArgs>? touchCapturedHandler;
    private CancellationTokenSource? pumpCts;
    private Task? pumpTask;
    private bool disposed;

    public PairingSessionTouchCaptureStreamer(PairingSessionTransport transport)
    {
        this.transport = transport;
    }

    public async Task<OperationResult> StartAsync(
        TouchCaptureHub touchCaptureHub,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(touchCaptureHub);

        if (!touchCaptureHub.IsEnabled)
        {
            return OperationResult.FromSuccess("Touch capture is not enabled.");
        }

        if (!transport.IsOpen)
        {
            return OperationResult.FromFailure("WebSocket session is not open.");
        }

        await StopAsync(progress: null, CancellationToken.None);

        lock (touchLock)
        {
            pendingTouches.Clear();
            this.touchCaptureHub = touchCaptureHub;
        }

        touchCapturedHandler = (_, args) =>
        {
            lock (touchLock)
            {
                pendingTouches.Add(args.Touch);
                if (pendingTouches.Count > MaxPendingTouches)
                {
                    pendingTouches.RemoveRange(0, pendingTouches.Count - MaxPendingTouches);
                }
            }

            signal.Release();
        };

        touchCaptureHub.TouchCaptured += touchCapturedHandler;
        pumpCts = new CancellationTokenSource();
        pumpTask = Task.Run(() => RunPumpAsync(progress, pumpCts.Token));

        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.TouchCapture,
            "Touch capture streaming started.",
            source: HostConnectionSource.TouchCapture);
        return OperationResult.FromSuccess("Touch capture streaming started.");
    }

    public async Task<OperationResult> StopAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        TouchCaptureHub? touchCaptureHub;
        EventHandler<TouchCapturedEventArgs>? touchCapturedHandler;
        Task? pumpTask;
        CancellationTokenSource? pumpCts;

        lock (touchLock)
        {
            touchCaptureHub = this.touchCaptureHub;
            touchCapturedHandler = this.touchCapturedHandler;
            pumpTask = this.pumpTask;
            pumpCts = this.pumpCts;
            this.touchCaptureHub = null;
            this.touchCapturedHandler = null;
            this.pumpTask = null;
            this.pumpCts = null;
            pendingTouches.Clear();
        }

        if (touchCaptureHub is not null && touchCapturedHandler is not null)
        {
            touchCaptureHub.TouchCaptured -= touchCapturedHandler;
        }

        pumpCts?.Cancel();

        var currentTaskId = Task.CurrentId;
        if (pumpTask is not null && (!currentTaskId.HasValue || pumpTask.Id != currentTaskId.Value))
        {
            try
            {
                await pumpTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
            catch
            {
                // Ignore pump errors while stopping.
            }
        }

        pumpCts?.Dispose();

        HostPairingProgressReporter.Report(
            progress,
            HostConnectionProgressKind.TouchCapture,
            "Touch capture streaming stopped.",
            source: HostConnectionSource.TouchCapture);
        return OperationResult.FromSuccess("Touch capture streaming stopped.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        pumpCts?.Cancel();

        if (touchCaptureHub is not null && touchCapturedHandler is not null)
        {
            touchCaptureHub.TouchCaptured -= touchCapturedHandler;
        }

        pumpCts?.Dispose();
        signal.Dispose();
        touchCaptureHub = null;
        touchCapturedHandler = null;
        pumpTask = null;
        pumpCts = null;
    }

    private async Task RunPumpAsync(IProgress<HostConnectionProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                CapturedTouch[] batch;

                lock (touchLock)
                {
                    if (pendingTouches.Count == 0)
                    {
                        break;
                    }

                    var batchSize = Math.Min(pendingTouches.Count, MaxBatchSize);
                    batch = pendingTouches.Take(batchSize).ToArray();
                    pendingTouches.RemoveRange(0, batchSize);
                }

                if (batch.Length == 0)
                {
                    break;
                }

                var result = await SendBatchAsync(batch, progress, cancellationToken);
                if (!result.Success)
                {
                    HostPairingProgressReporter.Report(
                        progress,
                        HostConnectionProgressKind.Warning,
                        $"Touch capture streaming stopped: {result.Message}",
                        source: HostConnectionSource.TouchCapture);
                    return;
                }
            }
        }
    }

    private async Task<OperationResult> SendBatchAsync(
        IReadOnlyList<CapturedTouch> touches,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (touches.Count == 0)
        {
            return OperationResult.FromSuccess("No touches to stream.");
        }

        var batches = BuildBatches(touches);
        foreach (var batch in batches)
        {
            var payload = JsonSerializer.Serialize(new
            {
                type = TouchInputType,
                schema = TouchSchemaName,
                t0 = batch.T0,
                space = batch.Space,
                unit = batch.Unit,
                surface = batch.Surface,
                rows = batch.Rows
            }, PairingJson.Compact);

            var result = await transport.SendTextAsync(payload, cancellationToken);
            if (!result.Success)
            {
                return result;
            }
        }

        if (progress is not null)
        {
            HostPairingProgressReporter.Report(
                progress,
                HostConnectionProgressKind.TouchCapture,
                $"WS -> streamed {touches.Count} touch input records",
                isVerbose: true,
                source: HostConnectionSource.TouchCapture);
        }

        return OperationResult.FromSuccess($"Streamed {touches.Count} touch input records.");
    }

    private static List<TouchCapturePackedBatch> BuildBatches(IReadOnlyList<CapturedTouch> touches)
    {
        return touches
            .OrderBy(touch => touch.CapturedAtUtc)
            .ThenBy(touch => touch.Id)
            .GroupBy(CreateBatchKey)
            .Select(CreateBatch)
            .ToList();
    }

    private static TouchCaptureBatchKey CreateBatchKey(CapturedTouch touch)
    {
        return new TouchCaptureBatchKey(
            EncodeSpace(touch.CoordinateSpace),
            EncodeUnit(touch.CoordinateUnit),
            touch.SurfaceWidth,
            touch.SurfaceHeight,
            touch.SurfaceScale);
    }

    private static TouchCapturePackedBatch CreateBatch(IGrouping<TouchCaptureBatchKey, CapturedTouch> group)
    {
        var touches = group
            .OrderBy(touch => touch.CapturedAtUtc)
            .ThenBy(touch => touch.Id)
            .ToArray();
        var t0 = touches[0].CapturedAtUtc.ToUniversalTime();
        var rows = new List<object?[]>(touches.Length);
        foreach (var touch in touches)
        {
            var deltaMs = (long)Math.Round(
                (touch.CapturedAtUtc.ToUniversalTime() - t0).TotalMilliseconds,
                MidpointRounding.AwayFromZero);
            var row = new List<object?>
            {
                Math.Max(0L, deltaMs),
                SerializeAction(touch.Action),
                touch.PointerId,
                touch.X,
                touch.Y
            };
            if (touch.PointerIndex != 0 || touch.PointerCount != 1)
            {
                row.Add(touch.PointerIndex);
                row.Add(touch.PointerCount);
            }

            rows.Add(row.ToArray());
        }

        return new TouchCapturePackedBatch(
            t0,
            group.Key.Space,
            group.Key.Unit,
            [group.Key.SurfaceWidth, group.Key.SurfaceHeight, group.Key.SurfaceScale],
            rows);
    }

    private static string EncodeSpace(string? coordinateSpace)
    {
        return string.Equals(coordinateSpace?.Trim(), "window", StringComparison.OrdinalIgnoreCase)
            ? WindowSpaceCode
            : string.IsNullOrWhiteSpace(coordinateSpace)
                ? WindowSpaceCode
                : coordinateSpace.Trim();
    }

    private static string EncodeUnit(string? coordinateUnit)
    {
        var normalized = coordinateUnit?.Trim();
        return normalized?.ToLowerInvariant() switch
        {
            "pixels" or "pixel" or "px" => PixelUnitCode,
            "points" or "point" or "pt" => PointUnitCode,
            "normalized" or "unit" or "ratio" or "n" => "n",
            _ => string.IsNullOrWhiteSpace(normalized) ? PixelUnitCode : normalized!
        };
    }

    private static int SerializeAction(CapturedTouchAction action)
    {
        return action switch
        {
            CapturedTouchAction.Down => ActionDown,
            CapturedTouchAction.Move => ActionMove,
            CapturedTouchAction.Up => ActionUp,
            CapturedTouchAction.Cancel => ActionCancel,
            _ => ActionUnknown
        };
    }

}
