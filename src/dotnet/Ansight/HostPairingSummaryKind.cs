namespace Ansight;

/// <summary>
/// Categorizes high-level host pairing status summaries.
/// </summary>
public enum HostPairingSummaryKind
{
    /// <summary>
    /// The runtime has not been initialized, so host pairing is unavailable.
    /// </summary>
    RuntimeUnavailable = 0,

    /// <summary>
    /// The runtime exists but is inactive, so no pairing flow is currently running.
    /// </summary>
    RuntimeInactive = 1,

    /// <summary>
    /// The runtime is disconnected and no cached, stored, or bundled profiles are available.
    /// </summary>
    DisconnectedNoProfiles = 2,

    /// <summary>
    /// The runtime is disconnected and a cached host profile is available for reconnect.
    /// </summary>
    DisconnectedCachedProfileAvailable = 3,

    /// <summary>
    /// The runtime is disconnected and a stored preferred pairing profile is available.
    /// </summary>
    DisconnectedStoredProfileAvailable = 4,

    /// <summary>
    /// The runtime is disconnected and a bundled pairing profile is available.
    /// </summary>
    DisconnectedBundledProfileAvailable = 5,

    /// <summary>
    /// The runtime is disconnected and more than one profile source is available.
    /// </summary>
    DisconnectedMultipleProfilesAvailable = 6,

    /// <summary>
    /// A runtime-owned host pairing or connection operation is currently in progress.
    /// </summary>
    Connecting = 7,

    /// <summary>
    /// A live Ansight host session is currently connected.
    /// </summary>
    Connected = 8
}
