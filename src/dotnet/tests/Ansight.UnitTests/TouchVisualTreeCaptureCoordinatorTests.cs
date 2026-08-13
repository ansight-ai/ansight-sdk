using System.Collections.Concurrent;
using Ansight.Input;
using Ansight.Screenshot;

namespace Ansight.UnitTests;

public sealed class TouchVisualTreeCaptureCoordinatorTests
{
    [Fact]
    public async Task GestureCapturesLeadingCheckpointAndTerminalTrees()
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
            TimeSpan.FromMilliseconds(20));
        var hub = new TouchCaptureHub(new TouchCaptureOptions());
        coordinator.Start(hub);

        hub.Record(CreateTouch(CapturedTouchAction.Down, pointerId: 7));
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(await captured.WaitAsync(TimeSpan.FromSeconds(1)));
        hub.Record(CreateTouch(CapturedTouchAction.Up, pointerId: 7));
        await WaitForTerminalTriggerAsync(triggers);

        await coordinator.StopAsync(CancellationToken.None);
        var phases = triggers.Select(trigger => trigger.GesturePhase).ToArray();
        Assert.Equal(TouchVisualTreeGesturePhase.Started, phases[0]);
        Assert.Contains(TouchVisualTreeGesturePhase.Checkpoint, phases);
        Assert.Equal(TouchVisualTreeGesturePhase.Ended, phases[^1]);
        Assert.Single(triggers.Select(trigger => trigger.GestureId).Distinct(StringComparer.Ordinal));
    }

    private static async Task WaitForTerminalTriggerAsync(
        ConcurrentQueue<TouchVisualTreeCaptureTrigger> triggers)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!triggers.Any(trigger => trigger.GesturePhase == TouchVisualTreeGesturePhase.Ended))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
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
