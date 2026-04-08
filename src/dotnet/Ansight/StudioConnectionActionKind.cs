namespace Ansight;

/// <summary>
/// Identifies the Studio connection action that produced a result.
/// </summary>
public enum StudioConnectionActionKind
{
    /// <summary>
    /// No specific Studio connection action was associated with the result.
    /// </summary>
    None = 0,

    /// <summary>
    /// Open a Studio connection using an explicitly supplied ticket, payload, file, or QR request.
    /// </summary>
    Connect = 1,

    /// <summary>
    /// Attempt to connect automatically using cached, saved, or bundled ticket sources.
    /// </summary>
    AutoConnect = 2,

    /// <summary>
    /// Attempt to connect using the saved ticket store.
    /// </summary>
    ConnectUsingSavedTicket = 3,

    /// <summary>
    /// Attempt to connect using a bundled ticket embedded in or supplied to the app.
    /// </summary>
    ConnectUsingBundledTicket = 4,

    /// <summary>
    /// Attempt to connect using a pairing ticket payload or compact pairing ticket code.
    /// </summary>
    ConnectFromPayload = 5,

    /// <summary>
    /// Disconnect the current live host session.
    /// </summary>
    Disconnect = 6,

    /// <summary>
    /// Clear saved and cached ticket data.
    /// </summary>
    ClearSavedTickets = 7,

    /// <summary>
    /// Attempt to reconnect using the runtime-cached session.
    /// </summary>
    ConnectUsingCachedSession = 8
}
