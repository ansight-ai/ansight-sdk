namespace Ansight;

/// <summary>
/// Controls runtime-owned Ansight pairing profiles and profile-based connection flows.
/// </summary>
public interface IHostPairing
{
    /// <summary>
    /// Indicates whether a preferred pairing profile is stored locally for the current app.
    /// </summary>
    bool HasPreferredProfile { get; }

    /// <summary>
    /// Indicates whether a live Ansight host session is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// The latest runtime-owned host pairing status snapshot.
    /// </summary>
    HostPairingStatusSnapshot Status { get; }

    /// <summary>
    /// The latest runtime-owned host pairing capability snapshot.
    /// </summary>
    HostPairingCapabilities Capabilities { get; }

    /// <summary>
    /// Raised when the runtime-owned host pairing status or capabilities change.
    /// </summary>
    event EventHandler<HostPairingStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Refreshes the pairing capability snapshot, including bundled profile availability when supported.
    /// </summary>
    Task<HostPairingCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the configured payload reader can handle the specified request kind.
    /// </summary>
    bool CanReadPayload(HostPairingPayloadReadKind kind);

    /// <summary>
    /// Attempts to connect using the runtime cached host profile first, then falls back to stored and bundled pairing profiles.
    /// </summary>
    Task<HostPairingActionResult> AutoConnectAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect using stored pairing profiles without using bundled fallback unless the stored profile is rejected and must be cleared.
    /// </summary>
    Task<HostPairingActionResult> ConnectUsingStoredProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect using configured bundled pairing profiles.
    /// </summary>
    Task<HostPairingActionResult> ConnectUsingBundledProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects using a QR payload, bootstrap document, or full pairing config payload.
    /// </summary>
    Task<HostPairingActionResult> ConnectFromPayloadAsync(
        string payload,
        string? sourceDescription = null,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses the configured payload reader to obtain a pairing payload and connect through the runtime-owned pairing flow.
    /// </summary>
    Task<HostPairingActionResult> ConnectFromPayloadReaderAsync(
        HostPairingPayloadReadRequest request,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the current host pairing session.
    /// </summary>
    Task<HostPairingActionResult> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears both the stored preferred pairing profile and the runtime cached host profile.
    /// </summary>
    HostPairingActionResult ClearStoredProfiles();
}
