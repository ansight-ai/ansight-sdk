namespace Ansight;

/// <summary>
/// Categorizes high-level Studio connection status summaries.
/// </summary>
public enum StudioConnectionSummaryKind
{
    /// <summary>
    /// The runtime has not been initialized, so Studio connection is unavailable.
    /// </summary>
    RuntimeUnavailable = 0,

    /// <summary>
    /// The runtime exists but is inactive, so no Studio connection flow is currently running.
    /// </summary>
    RuntimeInactive = 1,

    /// <summary>
    /// The runtime is disconnected and no cached, saved, or bundled tickets are available.
    /// </summary>
    DisconnectedNoTickets = 2,

    /// <summary>
    /// The runtime is disconnected and a cached session is available for reconnect.
    /// </summary>
    DisconnectedCachedSessionAvailable = 3,

    /// <summary>
    /// The runtime is disconnected and a saved ticket is available.
    /// </summary>
    DisconnectedSavedTicketAvailable = 4,

    /// <summary>
    /// The runtime is disconnected and a bundled ticket is available.
    /// </summary>
    DisconnectedBundledTicketAvailable = 5,

    /// <summary>
    /// The runtime is disconnected and more than one ticket source is available.
    /// </summary>
    DisconnectedMultipleTicketsAvailable = 6,

    /// <summary>
    /// A runtime-owned Studio connection operation is currently in progress.
    /// </summary>
    Connecting = 7,

    /// <summary>
    /// A live Ansight host session is currently connected.
    /// </summary>
    Connected = 8
}
