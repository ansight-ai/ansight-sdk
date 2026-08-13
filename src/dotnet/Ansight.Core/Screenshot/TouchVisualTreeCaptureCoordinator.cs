using Ansight.Input;

namespace Ansight.Screenshot;

internal enum TouchVisualTreeGesturePhase
{
    Started,
    Checkpoint,
    Ended,
    Cancelled
}

internal sealed record TouchVisualTreeCaptureTrigger(
    string GestureId,
    CapturedTouchAction TouchAction,
    TouchVisualTreeGesturePhase GesturePhase,
    DateTimeOffset TouchCapturedAtUtc);

internal sealed class TouchVisualTreeCaptureCoordinator : IDisposable
{
    internal static readonly TimeSpan CheckpointInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<TouchVisualTreeCaptureTrigger, CancellationToken, Task> captureAsync;
    private readonly TimeSpan checkpointInterval;
    private readonly Lock stateLock = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly HashSet<long> activePointerIds = [];
    private readonly Queue<TouchVisualTreeCaptureTrigger> pendingTriggers = [];
    private TouchCaptureHub? touchCaptureHub;
    private EventHandler<TouchCapturedEventArgs>? touchCapturedHandler;
    private EventHandler? runtimeCaptureInterruptedHandler;
    private EventHandler<AppLifecycleStateChangedEventArgs>? appLifecycleStateChangedHandler;
    private CancellationTokenSource? captureCts;
    private Task? captureTask;
    private Task? checkpointTask;
    private CapturedTouch? latestTouch;
    private string? gestureId;
    private int gestureGeneration;
    private bool disposed;

    public TouchVisualTreeCaptureCoordinator(
        Func<TouchVisualTreeCaptureTrigger, CancellationToken, Task> captureAsync,
        TimeSpan? checkpointInterval = null)
    {
        this.captureAsync = captureAsync ?? throw new ArgumentNullException(nameof(captureAsync));
        this.checkpointInterval = checkpointInterval.GetValueOrDefault(CheckpointInterval);
        if (this.checkpointInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointInterval));
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
        Task? checkpointTask;

        lock (stateLock)
        {
            touchCaptureHub = this.touchCaptureHub;
            touchCapturedHandler = this.touchCapturedHandler;
            runtimeCaptureInterruptedHandler = this.runtimeCaptureInterruptedHandler;
            appLifecycleStateChangedHandler = this.appLifecycleStateChangedHandler;
            captureCts = this.captureCts;
            captureTask = this.captureTask;
            checkpointTask = this.checkpointTask;
            this.touchCaptureHub = null;
            this.touchCapturedHandler = null;
            this.runtimeCaptureInterruptedHandler = null;
            this.appLifecycleStateChangedHandler = null;
            this.captureCts = null;
            this.captureTask = null;
            this.checkpointTask = null;
            latestTouch = null;
            gestureId = null;
            gestureGeneration++;
            activePointerIds.Clear();
            pendingTriggers.Clear();
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
        await WaitForTaskAsync(checkpointTask);
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
        CancellationToken cancellationToken;
        int checkpointGeneration = 0;

        lock (stateLock)
        {
            if (captureCts is null || captureCts.IsCancellationRequested)
            {
                return;
            }

            cancellationToken = captureCts.Token;
            latestTouch = touch;
            switch (touch.Action)
            {
                case CapturedTouchAction.Down:
                    var beginsGesture = activePointerIds.Count == 0;
                    activePointerIds.Add(touch.PointerId);
                    if (beginsGesture)
                    {
                        gestureId = $"gesture-{Guid.NewGuid():N}";
                        gestureGeneration++;
                        checkpointGeneration = gestureGeneration;
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
                    EnqueueLocked(CreateTrigger(touch, TouchVisualTreeGesturePhase.Cancelled));
                    break;
            }
        }

        if (checkpointGeneration != 0)
        {
            var task = Task.Run(() => RunCheckpointLoopAsync(checkpointGeneration, cancellationToken));
            lock (stateLock)
            {
                if (gestureGeneration == checkpointGeneration)
                {
                    checkpointTask = task;
                }
            }
        }
    }

    private void ResetGesture()
    {
        lock (stateLock)
        {
            latestTouch = null;
            gestureId = null;
            gestureGeneration++;
            activePointerIds.Clear();
            pendingTriggers.Clear();
        }
    }

    private async Task RunCheckpointLoopAsync(int generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkpointInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (stateLock)
            {
                if (generation != gestureGeneration || activePointerIds.Count == 0 || latestTouch is null)
                {
                    return;
                }

                EnqueueLocked(CreateTrigger(latestTouch, TouchVisualTreeGesturePhase.Checkpoint));
            }
        }
    }

    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
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
                TouchVisualTreeCaptureTrigger? trigger;
                lock (stateLock)
                {
                    trigger = pendingTriggers.Count == 0 ? null : pendingTriggers.Dequeue();
                }

                if (trigger is null)
                {
                    break;
                }

                try
                {
                    await captureAsync(trigger, cancellationToken);
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
        if (trigger.GesturePhase == TouchVisualTreeGesturePhase.Checkpoint
            && pendingTriggers.Any(candidate => candidate.GesturePhase == TouchVisualTreeGesturePhase.Checkpoint))
        {
            return;
        }

        pendingTriggers.Enqueue(trigger);
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
