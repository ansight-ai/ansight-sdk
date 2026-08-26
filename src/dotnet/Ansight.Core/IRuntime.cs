namespace Ansight;

using Ansight.Tools;

/// <summary>
/// Contract for controlling the Ansight telemetry runtime.
/// </summary>
public interface IRuntime
{
    /// <summary>
    /// The backing data sink being used by Ansight.
    /// </summary>
    IDataSink DataSink { get; }

    /// <summary>
    /// The protocol bridge used to query and execute registered tools.
    /// </summary>
    ToolProtocolBridge ToolBridge { get; }

    /// <summary>
    /// Controls the unified runtime-owned host connection surface.
    /// </summary>
    IHostConnection HostConnection { get; }

    /// <summary>
    /// True when periodic sampling is active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Indicates whether FPS tracking is currently enabled.
    /// </summary>
    bool IsFramesPerSecondEnabled { get; }

    /// <summary>
    /// Indicates whether touch capture is configured and allowed by the runtime-level capture toggle.
    /// </summary>
    bool IsTouchCaptureEnabled { get; }

    /// <summary>
    /// Raised after activation finishes and sampling begins.
    /// </summary>
    event EventHandler OnActivated;

    /// <summary>
    /// Raised after deactivation finishes and sampling stops.
    /// </summary>
    event EventHandler OnDeactivated;

    /// <summary>
    /// Starts memory sampling and raises <see cref="OnActivated"/> when complete.
    /// </summary>
    void Activate();

    /// <summary>
    /// Stops memory sampling and raises <see cref="OnDeactivated"/> when complete.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Enables frames-per-second tracking.
    /// </summary>
    void EnableFramesPerSecond();

    /// <summary>
    /// Disables frames-per-second tracking.
    /// </summary>
    void DisableFramesPerSecond();

    /// <summary>
    /// Enables runtime-level touch capture emission when touch capture was configured at initialization.
    /// </summary>
    void EnableTouchCapture();

    /// <summary>
    /// Disables runtime-level touch capture emission without changing the runtime activation state.
    /// </summary>
    void DisableTouchCapture();

    /// <summary>
    /// Sets an optional runtime-level guard that is evaluated before each captured touch is emitted.
    /// Return <see langword="true"/> to allow capture, or <see langword="false"/> to suppress it.
    /// Pass <see langword="null"/> to clear the guard.
    /// </summary>
    void SetTouchCaptureGuard(Func<bool>? guard);

    /// <summary>
    /// Enables or disables sensitive-value redaction for subsequent network captures at runtime.
    /// Redaction is enabled by default. Disable it only for an explicitly trusted local capture.
    /// </summary>
    void SetNetworkCaptureRedactionEnabled(bool enabled);

    /// <summary>
    /// Captures a new metric using the given <paramref name="value"/> against the <paramref name="channel"/>.
    /// </summary>
    void Metric(long value, byte channel);

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the unspecified channel.
    /// </summary>
    void Event(string label);

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> and type against the unspecified channel.
    /// </summary>
    void Event(string label, AppEventType type);

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/>, <paramref name="type"/>, and <paramref name="details"/> against the unspecified channel.
    /// </summary>
    void Event(string label, AppEventType type, string details);

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/> against the <paramref name="channel"/>.
    /// </summary>
    void Event(string label, byte channel);

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/>, <paramref name="type"/>, and <paramref name="channel"/>.
    /// </summary>
    void Event(string label, AppEventType type, byte channel);

    /// <summary>
    /// Captures a new event using the given <paramref name="label"/>, <paramref name="type"/>, <paramref name="channel"/>, and <paramref name="details"/>.
    /// </summary>
    void Event(string label, AppEventType type, byte channel, string details);

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/> against the unspecified channel.
    /// </summary>
    void ScreenViewed(string screenName);

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/> and <paramref name="details"/> against the unspecified channel.
    /// </summary>
    void ScreenViewed(string screenName, string details);

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/> against the <paramref name="channel"/>.
    /// </summary>
    void ScreenViewed(string screenName, byte channel);

    /// <summary>
    /// Captures a screen-viewed event using the given <paramref name="screenName"/>, <paramref name="channel"/>, and <paramref name="details"/>.
    /// </summary>
    void ScreenViewed(string screenName, byte channel, string details);

    /// <summary>
    /// Registers or replaces a custom grouped property for current and future live pairing sessions.
    /// </summary>
    void RegisterCustomProperty(string group, string key, object? value);

    /// <summary>
    /// Removes a custom grouped property from current and future live pairing sessions.
    /// </summary>
    bool RemoveCustomProperty(string group, string key);

    /// <summary>
    /// Clears all custom grouped properties from current and future live pairing sessions.
    /// </summary>
    void ClearCustomProperties();

    /// <summary>
    /// Clears the backing data sink, removing all recorded metrics and events.
    /// </summary>
    void Clear();
}
