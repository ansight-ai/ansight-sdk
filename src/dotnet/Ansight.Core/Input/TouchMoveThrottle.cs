namespace Ansight.Input;

internal sealed class TouchMoveThrottle
{
    private readonly TouchCaptureOptions options;
    private readonly Dictionary<long, CapturedTouch> lastRecordedTouchByPointerId = new();

    public TouchMoveThrottle(TouchCaptureOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool ShouldRecord(CapturedTouch touch)
    {
        if (touch.Action != CapturedTouchAction.Move)
        {
            return true;
        }

        if (!lastRecordedTouchByPointerId.TryGetValue(touch.PointerId, out var previousTouch))
        {
            return true;
        }

        return HasReachedFrameInterval(previousTouch, touch)
            && HasMovedEnough(previousTouch, touch);
    }

    public void ObserveRecorded(CapturedTouch touch)
    {
        switch (touch.Action)
        {
            case CapturedTouchAction.Down:
            case CapturedTouchAction.Move:
                lastRecordedTouchByPointerId[touch.PointerId] = touch;
                break;
            case CapturedTouchAction.Up:
            case CapturedTouchAction.Cancel:
                lastRecordedTouchByPointerId.Remove(touch.PointerId);
                break;
        }
    }

    private bool HasReachedFrameInterval(CapturedTouch previousTouch, CapturedTouch touch)
    {
        var framesPerSecond = GetConfiguredFramesPerSecond();
        if (framesPerSecond == 0)
        {
            return true;
        }

        var minimumIntervalMilliseconds = 1000d / framesPerSecond;
        return (touch.CapturedAtUtc - previousTouch.CapturedAtUtc).TotalMilliseconds >= minimumIntervalMilliseconds;
    }

    private bool HasMovedEnough(CapturedTouch previousTouch, CapturedTouch touch)
    {
        var threshold = GetDistanceThreshold(touch);
        if (threshold == 0)
        {
            return true;
        }

        var deltaX = touch.X - previousTouch.X;
        var deltaY = touch.Y - previousTouch.Y;
        var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
        return distanceSquared >= threshold * threshold;
    }

    private int GetConfiguredFramesPerSecond()
    {
        return options.MoveCaptureFramesPerSecond < 0
            ? TouchCaptureOptions.DefaultMoveCaptureFramesPerSecond
            : options.MoveCaptureFramesPerSecond;
    }

    private double GetDistanceThreshold(CapturedTouch touch)
    {
        var threshold = options.MoveCaptureDistanceThreshold;
        if (!double.IsFinite(threshold) || threshold < 0)
        {
            threshold = TouchCaptureOptions.DefaultMoveCaptureDistanceThreshold;
        }

        if (threshold == 0)
        {
            return 0;
        }

        if (string.Equals(touch.CoordinateUnit, "pixels", StringComparison.OrdinalIgnoreCase)
            && touch.SurfaceScale is > 0
            && double.IsFinite(touch.SurfaceScale.Value))
        {
            return threshold * touch.SurfaceScale.Value;
        }

        return threshold;
    }
}
