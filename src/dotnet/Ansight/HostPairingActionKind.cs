namespace Ansight;

/// <summary>
/// Identifies the host pairing action that produced a result.
/// </summary>
public enum HostPairingActionKind
{
    /// <summary>
    /// No specific host pairing action was associated with the result.
    /// </summary>
    None = 0,

    /// <summary>
    /// Open a host connection using an explicitly supplied pairing document.
    /// </summary>
    Connect = 1,

    /// <summary>
    /// Attempt to connect automatically using cached, stored, or bundled pairing sources.
    /// </summary>
    AutoConnect = 2,

    /// <summary>
    /// Attempt to connect using the stored preferred pairing profile.
    /// </summary>
    ConnectUsingStoredProfile = 3,

    /// <summary>
    /// Attempt to connect using a bundled pairing profile embedded in or supplied to the app.
    /// </summary>
    ConnectUsingBundledProfile = 4,

    /// <summary>
    /// Attempt to connect using a QR payload, bootstrap document, or pairing config payload.
    /// </summary>
    ConnectFromPayload = 5,

    /// <summary>
    /// Disconnect the current live host session.
    /// </summary>
    Disconnect = 6,

    /// <summary>
    /// Clear stored and cached pairing profile data.
    /// </summary>
    ClearStoredProfiles = 7,

    /// <summary>
    /// Attempt to reconnect using the runtime-cached host profile.
    /// </summary>
    ConnectUsingCachedProfile = 8
}
