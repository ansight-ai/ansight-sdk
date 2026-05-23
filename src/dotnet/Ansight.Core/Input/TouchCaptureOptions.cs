namespace Ansight;

/// <summary>
/// Configures app-local touch capture while the Ansight runtime is active.
/// Touch capture records touch locations inside the current app window and can negatively affect runtime performance in touch-heavy views.
/// </summary>
public sealed class TouchCaptureOptions
{
    /// <summary>
    /// Whether touch move events should be captured in addition to down/up events.
    /// Move capture can produce a large number of records during drags, scrolling, and multi-touch gestures.
    /// </summary>
    public bool CaptureMoveEvents { get; set; } = true;

    /// <summary>
    /// Whether cancellation events should be captured when the platform cancels an active touch sequence.
    /// </summary>
    public bool CaptureCancelEvents { get; set; } = true;

    internal TouchCaptureOptions Clone()
    {
        return new TouchCaptureOptions
        {
            CaptureMoveEvents = CaptureMoveEvents,
            CaptureCancelEvents = CaptureCancelEvents
        };
    }
}
