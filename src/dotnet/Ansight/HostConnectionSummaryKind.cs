namespace Ansight;

/// <summary>
/// Categorizes high-level host connection status summaries.
/// </summary>
public enum HostConnectionSummaryKind
{
    /// <summary>
    /// The runtime has not been initialized, so host connection is unavailable.
    /// </summary>
    RuntimeUnavailable = 0,

    /// <summary>
    /// The runtime exists but is inactive, so no host connection flow is currently running.
    /// </summary>
    RuntimeInactive = 1,

    /// <summary>
    /// The runtime is disconnected and no cached, saved, or bundled configs are available.
    /// </summary>
    DisconnectedNoConfigs = 2,

    /// <summary>
    /// The runtime is disconnected and a cached session is available for reconnect.
    /// </summary>
    DisconnectedCachedSessionAvailable = 3,

    /// <summary>
    /// The runtime is disconnected and a saved config is available.
    /// </summary>
    DisconnectedSavedConfigAvailable = 4,

    /// <summary>
    /// The runtime is disconnected and a bundled config is available.
    /// </summary>
    DisconnectedBundledConfigAvailable = 5,

    /// <summary>
    /// The runtime is disconnected and more than one config source is available.
    /// </summary>
    DisconnectedMultipleConfigsAvailable = 6,

    /// <summary>
    /// A runtime-owned host connection operation is currently in progress.
    /// </summary>
    Connecting = 7,

    /// <summary>
    /// A live Ansight host session is currently connected.
    /// </summary>
    Connected = 8
}
