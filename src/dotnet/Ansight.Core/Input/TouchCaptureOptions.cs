namespace Ansight;

/// <summary>
/// Configures app-local touch capture while the Ansight runtime is active.
/// Touch capture records touch locations inside the current app window and can negatively affect runtime performance in touch-heavy views.
/// </summary>
public sealed class TouchCaptureOptions
{
    /// <summary>
    /// Default minimum movement, in logical display units, required before another move event is captured for a pointer.
    /// </summary>
    public const double DefaultMoveCaptureDistanceThreshold = 4d;

    /// <summary>
    /// Default maximum move capture cadence per pointer.
    /// </summary>
    public const int DefaultMoveCaptureFramesPerSecond = 15;

    /// <summary>
    /// Whether touch move events should be captured in addition to down/up events.
    /// Move capture can produce a large number of records during drags, scrolling, and multi-touch gestures.
    /// </summary>
    public bool CaptureMoveEvents { get; set; } = true;

    /// <summary>
    /// Whether cancellation events should be captured when the platform cancels an active touch sequence.
    /// </summary>
    public bool CaptureCancelEvents { get; set; } = true;

    /// <summary>
    /// Minimum movement, in logical display units, required before another move event is captured for a pointer.
    /// Set to 0 to disable distance filtering.
    /// </summary>
    public double MoveCaptureDistanceThreshold { get; set; } = DefaultMoveCaptureDistanceThreshold;

    /// <summary>
    /// Maximum move capture cadence per pointer. Set to 0 to disable FPS filtering.
    /// </summary>
    public int MoveCaptureFramesPerSecond { get; set; } = DefaultMoveCaptureFramesPerSecond;

    internal TouchCaptureOptions Clone()
    {
        return new TouchCaptureOptions
        {
            CaptureMoveEvents = CaptureMoveEvents,
            CaptureCancelEvents = CaptureCancelEvents,
            MoveCaptureDistanceThreshold = MoveCaptureDistanceThreshold,
            MoveCaptureFramesPerSecond = MoveCaptureFramesPerSecond
        };
    }
}
