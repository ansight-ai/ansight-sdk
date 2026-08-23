using System.Diagnostics;
using Ansight.Input;

namespace Ansight.Screenshot;

internal enum TouchVisualTreeGesturePhase
{
    Started,
    Checkpoint,
    Ended
}

internal sealed record TouchVisualTreeCaptureTrigger(
    string GestureId,
    CapturedTouchAction TouchAction,
    TouchVisualTreeGesturePhase GesturePhase,
    DateTimeOffset TouchCapturedAtUtc);

internal sealed class TouchVisualTreeCaptureCoordinator : IDisposable
{
    internal static readonly TimeSpan DefaultMinimumCaptureInterval = TimeSpan.FromMilliseconds(750);

    private readonly Func<TouchVisualTreeCaptureTrigger, CancellationToken, Task> captureAsync;
    private readonly TimeSpan minimumCaptureInterval;
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly HashSet<long> activePointerIds = [];
    private TouchCaptureHub? touchCaptureHub;
    private EventHandler<TouchCapturedEventArgs>? touchCapturedHandler;
    private EventHandler? runtimeCaptureInterruptedHandler;
    private EventHandler<AppLifecycleStateChangedEventArgs>? appLifecycleStateChangedHandler;
    private CancellationTokenSource? captureCts;
    private Task? captureTask;
    private TouchVisualTreeCaptureTrigger? pendingTrigger;
    private string? gestureId;
    private bool disposed;

    public TouchVisualTreeCaptureCoordinator(
        Func<TouchVisualTreeCaptureTrigger, CancellationToken, Task> captureAsync,
        TimeSpan? minimumCaptureInterval = null)
    {
        this.captureAsync = captureAsync ?? throw new ArgumentNullException(nameof(captureAsync));
        this.minimumCaptureInterval = minimumCaptureInterval.GetValueOrDefault(DefaultMinimumCaptureInterval);
        if (this.minimumCaptureInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCaptureInterval));
        }
    }

    public void Start(TouchCaptureHub touchCaptureHub)
    {
        ArgumentNullException.ThrowIfNull(touchCaptureHub);
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (stateLock)
        {
            if (captureCts is not null)
            {
                return;
            }

            this.touchCaptureHub = touchCaptureHub;
            captureCts = new CancellationTokenSource();
            touchCapturedHandler = (_, args) => Observe(args.Touch);
            runtimeCaptureInterruptedHandler = (_, _) => ResetGesture();
            appLifecycleStateChangedHandler = (_, args) =>
            {
                if (args.State == AppLifecycleState.Background)
                {
                    ResetGesture();
                }
            };
            touchCaptureHub.TouchCaptured += touchCapturedHandler;
            touchCaptureHub.RuntimeCaptureInterrupted += runtimeCaptureInterruptedHandler;
            Runtime.AppLifecycleStateChanged += appLifecycleStateChangedHandler;
            captureTask = Task.Run(() => RunCaptureLoopAsync(captureCts.Token));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        TouchCaptureHub? touchCaptureHub;
        EventHandler<TouchCapturedEventArgs>? touchCapturedHandler;
        EventHandler? runtimeCaptureInterruptedHandler;
        EventHandler<AppLifecycleStateChangedEventArgs>? appLifecycleStateChangedHandler;
        CancellationTokenSource? captureCts;
        Task? captureTask;

        lock (stateLock)
        {
            touchCaptureHub = this.touchCaptureHub;
            touchCapturedHandler = this.touchCapturedHandler;
            runtimeCaptureInterruptedHandler = this.runtimeCaptureInterruptedHandler;
            appLifecycleStateChangedHandler = this.appLifecycleStateChangedHandler;
            captureCts = this.captureCts;
            captureTask = this.captureTask;
            this.touchCaptureHub = null;
            this.touchCapturedHandler = null;
            this.runtimeCaptureInterruptedHandler = null;
            this.appLifecycleStateChangedHandler = null;
            this.captureCts = null;
            this.captureTask = null;
            gestureId = null;
            activePointerIds.Clear();
            pendingTrigger = null;
        }

        if (touchCaptureHub is not null && touchCapturedHandler is not null)
        {
            touchCaptureHub.TouchCaptured -= touchCapturedHandler;
        }
        if (touchCaptureHub is not null && runtimeCaptureInterruptedHandler is not null)
        {
            touchCaptureHub.RuntimeCaptureInterrupted -= runtimeCaptureInterruptedHandler;
        }
        if (appLifecycleStateChangedHandler is not null)
        {
            Runtime.AppLifecycleStateChanged -= appLifecycleStateChangedHandler;
        }

        captureCts?.Cancel();
        await WaitForTaskAsync(captureTask);
        captureCts?.Dispose();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (touchCaptureHub is not null && touchCapturedHandler is not null)
        {
            touchCaptureHub.TouchCaptured -= touchCapturedHandler;
        }
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        signal.Dispose();
    }

    private void Observe(CapturedTouch touch)
    {
        lock (stateLock)
        {
            if (captureCts is null || captureCts.IsCancellationRequested)
            {
                return;
            }

            switch (touch.Action)
            {
                case CapturedTouchAction.Down:
                    var beginsGesture = activePointerIds.Count == 0;
                    activePointerIds.Add(touch.PointerId);
                    if (beginsGesture)
                    {
                        gestureId = $"gesture-{Guid.NewGuid():N}";
                    }

                    EnqueueLocked(CreateTrigger(
                        touch,
                        beginsGesture
                            ? TouchVisualTreeGesturePhase.Started
                            : TouchVisualTreeGesturePhase.Checkpoint));
                    break;
                case CapturedTouchAction.Move:
                    activePointerIds.Add(touch.PointerId);
                    break;
                case CapturedTouchAction.Up:
                    activePointerIds.Remove(touch.PointerId);
                    EnqueueLocked(CreateTrigger(
                        touch,
                        activePointerIds.Count == 0
                            ? TouchVisualTreeGesturePhase.Ended
                            : TouchVisualTreeGesturePhase.Checkpoint));
                    break;
                case CapturedTouchAction.Cancel:
                    activePointerIds.Clear();
                    gestureId = null;
                    break;
            }
        }
    }

    private void ResetGesture()
    {
        lock (stateLock)
        {
            gestureId = null;
            activePointerIds.Clear();
            pendingTrigger = null;
        }
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        var minimumIntervalTimestampTicks = Math.Max(
            1,
            (long)Math.Ceiling(minimumCaptureInterval.TotalSeconds * Stopwatch.Frequency));
        long nextCaptureAllowedTimestamp = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                bool hasPendingTrigger;
                lock (stateLock)
                {
                    hasPendingTrigger = pendingTrigger is not null;
                }

                if (!hasPendingTrigger)
                {
                    break;
                }

                var remainingTimestampTicks = nextCaptureAllowedTimestamp - Stopwatch.GetTimestamp();
                if (remainingTimestampTicks > 0)
                {
                    try
                    {
                        var delay = TimeSpan.FromSeconds(
                            remainingTimestampTicks / (double)Stopwatch.Frequency);
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                TouchVisualTreeCaptureTrigger? trigger;
                lock (stateLock)
                {
                    trigger = pendingTrigger;
                    pendingTrigger = null;
                }

                if (trigger is null)
                {
                    continue;
                }

                try
                {
                    await captureAsync(trigger, cancellationToken);
                    nextCaptureAllowedTimestamp = Stopwatch.GetTimestamp() + minimumIntervalTimestampTicks;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Touch visual-tree capture skipped: {ex.Message}");
                }
            }
        }
    }

    private TouchVisualTreeCaptureTrigger CreateTrigger(
        CapturedTouch touch,
        TouchVisualTreeGesturePhase phase)
    {
        return new TouchVisualTreeCaptureTrigger(
            gestureId ?? $"gesture-{Guid.NewGuid():N}",
            touch.Action,
            phase,
            touch.CapturedAtUtc);
    }

    private void EnqueueLocked(TouchVisualTreeCaptureTrigger trigger)
    {
        pendingTrigger = SelectPendingTrigger(pendingTrigger, trigger);
        if (signal.CurrentCount == 0)
        {
            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private static TouchVisualTreeCaptureTrigger SelectPendingTrigger(
        TouchVisualTreeCaptureTrigger? pending,
        TouchVisualTreeCaptureTrigger incoming)
    {
        if (pending is null || incoming.GesturePhase == TouchVisualTreeGesturePhase.Started)
        {
            return incoming;
        }

        if (pending.GesturePhase == TouchVisualTreeGesturePhase.Started)
        {
            return pending;
        }

        return incoming.GesturePhase == TouchVisualTreeGesturePhase.Ended
            ? incoming
            : pending;
    }

    private static async Task WaitForTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
