using System.Collections.Concurrent;
using Ansight.Input;
using Ansight.Screenshot;

namespace Ansight.UnitTests;

public sealed class TouchVisualTreeCaptureCoordinatorTests
{
    [Fact]
    public async Task GestureCapturesOnlyDownAndUpTrees()
    {
        var triggers = new ConcurrentQueue<TouchVisualTreeCaptureTrigger>();
        using var captured = new SemaphoreSlim(0);
        using var coordinator = new TouchVisualTreeCaptureCoordinator(
            (trigger, _) =>
            {
                triggers.Enqueue(trigger);
                captured.Release();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(10));
        var hub = new TouchCaptureHub(new TouchCaptureOptions());
        coordinator.Start(hub);

        hub.Record(CreateTouch(CapturedTouchAction.Down, pointerId: 7));
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));
        hub.Record(CreateTouch(CapturedTouchAction.Move, pointerId: 7));
        Assert.False(await captured.WaitAsync(TimeSpan.FromMilliseconds(350)));
        hub.Record(CreateTouch(CapturedTouchAction.Up, pointerId: 7));
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));

        await coordinator.StopAsync(CancellationToken.None);
        var capturedTriggers = triggers.ToArray();
        Assert.Collection(
            capturedTriggers,
            trigger =>
            {
                Assert.Equal(CapturedTouchAction.Down, trigger.TouchAction);
                Assert.Equal(TouchVisualTreeGesturePhase.Started, trigger.GesturePhase);
            },
            trigger =>
            {
                Assert.Equal(CapturedTouchAction.Up, trigger.TouchAction);
                Assert.Equal(TouchVisualTreeGesturePhase.Ended, trigger.GesturePhase);
            });
        Assert.Single(triggers.Select(trigger => trigger.GestureId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CancelDoesNotCaptureTree()
    {
        var triggers = new ConcurrentQueue<TouchVisualTreeCaptureTrigger>();
        using var captured = new SemaphoreSlim(0);
        using var coordinator = new TouchVisualTreeCaptureCoordinator(
            (trigger, _) =>
            {
                triggers.Enqueue(trigger);
                captured.Release();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(10));
        var hub = new TouchCaptureHub(new TouchCaptureOptions());
        coordinator.Start(hub);

        hub.Record(CreateTouch(CapturedTouchAction.Down, pointerId: 7));
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));
        hub.Record(CreateTouch(CapturedTouchAction.Cancel, pointerId: 7));
        Assert.False(await captured.WaitAsync(TimeSpan.FromMilliseconds(100)));

        await coordinator.StopAsync(CancellationToken.None);
        Assert.Single(triggers);
    }

    [Fact]
    public async Task BusyCaptureCoalescesAContinuousGestureBurst()
    {
        var triggers = new ConcurrentQueue<TouchVisualTreeCaptureTrigger>();
        using var captured = new SemaphoreSlim(0);
        var releaseFirstCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new TouchVisualTreeCaptureCoordinator(
            async (trigger, cancellationToken) =>
            {
                triggers.Enqueue(trigger);
                captured.Release();
                if (triggers.Count == 1)
                {
                    await releaseFirstCapture.Task.WaitAsync(cancellationToken);
                }
            },
            TimeSpan.FromMilliseconds(20));
        var hub = new TouchCaptureHub(new TouchCaptureOptions());
        coordinator.Start(hub);

        hub.Record(CreateTouch(CapturedTouchAction.Down, pointerId: 1));
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));
        hub.Record(CreateTouch(CapturedTouchAction.Up, pointerId: 1));
        for (var pointerId = 2; pointerId <= 20; pointerId++)
        {
            hub.Record(CreateTouch(CapturedTouchAction.Down, pointerId));
            hub.Record(CreateTouch(CapturedTouchAction.Up, pointerId));
        }

        releaseFirstCapture.SetResult();
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(await captured.WaitAsync(TimeSpan.FromMilliseconds(100)));

        await coordinator.StopAsync(CancellationToken.None);
        var capturedTriggers = triggers.ToArray();
        Assert.Equal(2, capturedTriggers.Length);
        Assert.All(
            capturedTriggers,
            trigger => Assert.Equal(TouchVisualTreeGesturePhase.Started, trigger.GesturePhase));
        Assert.Equal(2, capturedTriggers.Select(trigger => trigger.GestureId).Distinct(StringComparer.Ordinal).Count());
    }

    private static CapturedTouch CreateTouch(CapturedTouchAction action, long pointerId)
    {
        return new CapturedTouch(
            action,
            pointerId,
            pointerIndex: 0,
            pointerCount: 1,
            x: 24,
            y: 48,
            surfaceWidth: 200,
            surfaceHeight: 400,
            coordinateUnit: "points",
            surfaceScale: 2,
            capturedAtUtc: DateTimeOffset.UtcNow);
    }
}
