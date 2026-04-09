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
    /// Clears the backing data sink, removing all recorded metrics and events.
    /// </summary>
    void Clear();
}
