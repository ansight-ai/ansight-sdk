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
        DateTimeOffset capturedAtUtc,
        TouchSampleDetails? details = null)
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
        Details = details?.WithoutNonFiniteValues();
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

    public TouchSampleDetails? Details { get; }

    public double? NormalizedX => SurfaceWidth > 0 ? X / SurfaceWidth.Value : null;

    public double? NormalizedY => SurfaceHeight > 0 ? Y / SurfaceHeight.Value : null;
}

internal sealed record TouchSampleDetails
{
    public required string Tool { get; init; }
    public string SampleKind { get; init; } = "current";
    public double? Force { get; init; }
    public double? MaximumPossibleForce { get; init; }
    public double? AltitudeRadians { get; init; }
    public double? AzimuthRadians { get; init; }
    public double? RollRadians { get; init; }
    public long? EstimatedProperties { get; init; }
    public long? EstimatedPropertiesExpectingUpdates { get; init; }
    public long? EstimationUpdateIndex { get; init; }
    public double? Pressure { get; init; }
    public double? TiltRadians { get; init; }
    public double? OrientationRadians { get; init; }
    public double? Distance { get; init; }
    public int? ButtonState { get; init; }
    public double? TouchMajor { get; init; }
    public double? TouchMinor { get; init; }
    public double? ToolMajor { get; init; }
    public double? ToolMinor { get; init; }

    public TouchSampleDetails WithoutNonFiniteValues()
    {
        static double? Finite(double? value) => value is double number && double.IsFinite(number)
            ? number
            : null;

        return this with
        {
            Force = Finite(Force),
            MaximumPossibleForce = Finite(MaximumPossibleForce),
            AltitudeRadians = Finite(AltitudeRadians),
            AzimuthRadians = Finite(AzimuthRadians),
            RollRadians = Finite(RollRadians),
            Pressure = Finite(Pressure),
            TiltRadians = Finite(TiltRadians),
            OrientationRadians = Finite(OrientationRadians),
            Distance = Finite(Distance),
            TouchMajor = Finite(TouchMajor),
            TouchMinor = Finite(TouchMinor),
            ToolMajor = Finite(ToolMajor),
            ToolMinor = Finite(ToolMinor)
        };
    }
}
