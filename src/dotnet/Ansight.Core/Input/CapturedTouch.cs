namespace Ansight.Input;

internal sealed class CapturedTouch
{
    public CapturedTouch(
        CapturedTouchAction action,
        long pointerId,
        int pointerIndex,
        int pointerCount,
        double x,
        double y,
        double? surfaceWidth,
        double? surfaceHeight,
        string coordinateUnit,
        double? surfaceScale,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinateUnit);

        Action = action;
        PointerId = pointerId;
        PointerIndex = pointerIndex;
        PointerCount = pointerCount;
        X = x;
        Y = y;
        SurfaceWidth = surfaceWidth;
        SurfaceHeight = surfaceHeight;
        CoordinateUnit = coordinateUnit.Trim();
        SurfaceScale = surfaceScale;
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; } = Guid.CreateVersion7();

    public CapturedTouchAction Action { get; }

    public long PointerId { get; }

    public int PointerIndex { get; }

    public int PointerCount { get; }

    public double X { get; }

    public double Y { get; }

    public double? SurfaceWidth { get; }

    public double? SurfaceHeight { get; }

    public string CoordinateSpace => "window";

    public string CoordinateUnit { get; }

    public double? SurfaceScale { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public double? NormalizedX => SurfaceWidth > 0 ? X / SurfaceWidth.Value : null;

    public double? NormalizedY => SurfaceHeight > 0 ? Y / SurfaceHeight.Value : null;
}
