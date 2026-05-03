namespace Ansight;

/// <summary>
/// Identifies the host connection action that produced a result.
/// </summary>
public enum HostConnectionActionKind
{
    /// <summary>
    /// No specific host connection action was associated with the result.
    /// </summary>
    None = 0,

    /// <summary>
    /// Open a host connection using an explicitly supplied config document, payload, file, or QR request.
    /// </summary>
    Connect = 1,

    /// <summary>
    /// Attempt to connect automatically using cached, saved, or bundled config sources.
    /// </summary>
    AutoConnect = 2,

    /// <summary>
    /// Attempt to connect using the saved config store.
    /// </summary>
    ConnectUsingSavedConfig = 3,

    /// <summary>
    /// Attempt to connect using a bundled config embedded in or supplied to the app.
    /// </summary>
    ConnectUsingBundledConfig = 4,

    /// <summary>
    /// Attempt to connect using a pairing config payload or compact pairing config code.
    /// </summary>
    ConnectFromPayload = 5,

    /// <summary>
    /// Disconnect the current live host session.
    /// </summary>
    Disconnect = 6,

    /// <summary>
    /// Clear saved and cached pairing config data.
    /// </summary>
    ClearSavedConfigs = 7,

    /// <summary>
    /// Attempt to reconnect using the runtime-cached session.
    /// </summary>
    ConnectUsingCachedSession = 8
}
