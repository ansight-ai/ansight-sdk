using Ansight.Input;

namespace Ansight.UnitTests;

public sealed class TouchMoveThrottleTests
{
    [Fact]
    public void ShouldRecord_ThrottlesMovesUntilDistanceAndFrameIntervalAreMet()
    {
        var options = new TouchCaptureOptions
        {
            MoveCaptureDistanceThreshold = 4,
            MoveCaptureFramesPerSecond = 30
        };
        var throttle = new TouchMoveThrottle(options);
        var start = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var down = CreateTouch(CapturedTouchAction.Down, x: 10, y: 10, capturedAtUtc: start);

        Assert.True(throttle.ShouldRecord(down));
        throttle.ObserveRecorded(down);

        Assert.False(throttle.ShouldRecord(CreateTouch(CapturedTouchAction.Move, x: 13, y: 10, capturedAtUtc: start.AddMilliseconds(40))));
        Assert.False(throttle.ShouldRecord(CreateTouch(CapturedTouchAction.Move, x: 16, y: 10, capturedAtUtc: start.AddMilliseconds(20))));
        Assert.True(throttle.ShouldRecord(CreateTouch(CapturedTouchAction.Move, x: 16, y: 10, capturedAtUtc: start.AddMilliseconds(40))));
    }

    [Fact]
    public void ShouldRecord_UsesSurfaceScaleForPixelDistanceThreshold()
    {
        var options = new TouchCaptureOptions
        {
            MoveCaptureDistanceThreshold = 4,
            MoveCaptureFramesPerSecond = 0
        };
        var throttle = new TouchMoveThrottle(options);
        var start = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var down = CreateTouch(CapturedTouchAction.Down, x: 10, y: 10, coordinateUnit: "pixels", surfaceScale: 2, capturedAtUtc: start);

        Assert.True(throttle.ShouldRecord(down));
        throttle.ObserveRecorded(down);

        Assert.False(throttle.ShouldRecord(CreateTouch(CapturedTouchAction.Move, x: 17, y: 10, coordinateUnit: "pixels", surfaceScale: 2, capturedAtUtc: start.AddSeconds(1))));
        Assert.True(throttle.ShouldRecord(CreateTouch(CapturedTouchAction.Move, x: 18, y: 10, coordinateUnit: "pixels", surfaceScale: 2, capturedAtUtc: start.AddSeconds(1))));
    }

    [Fact]
    public void ShouldRecord_AllowsDisablingMoveThrottleFilters()
    {
        var options = new TouchCaptureOptions
        {
            MoveCaptureDistanceThreshold = 0,
            MoveCaptureFramesPerSecond = 0
        };
        var throttle = new TouchMoveThrottle(options);
        var start = new DateTimeOffset(2026, 5, 23, 0, 0, 0, TimeSpan.Zero);
        var down = CreateTouch(CapturedTouchAction.Down, x: 10, y: 10, capturedAtUtc: start);

        Assert.True(throttle.ShouldRecord(down));
        throttle.ObserveRecorded(down);

        Assert.True(throttle.ShouldRecord(CreateTouch(CapturedTouchAction.Move, x: 10.1, y: 10, capturedAtUtc: start.AddMilliseconds(1))));
    }

    private static CapturedTouch CreateTouch(
        CapturedTouchAction action,
        double x,
        double y,
        string coordinateUnit = "points",
        double? surfaceScale = 1,
        DateTimeOffset capturedAtUtc = default)
    {
        return new CapturedTouch(
            action,
            pointerId: 1,
            pointerIndex: 0,
            pointerCount: 1,
            x,
            y,
            surfaceWidth: 100,
            surfaceHeight: 100,
            coordinateUnit,
            surfaceScale,
            capturedAtUtc == default ? DateTimeOffset.UtcNow : capturedAtUtc);
    }
}
