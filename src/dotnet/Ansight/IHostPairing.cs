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
    /// Attempts to connect using the runtime cached host profile first, then falls back to stored and bundled pairing profiles.
    /// </summary>
    Task<HostPairingActionResult> AutoConnectAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect using stored pairing profiles without using bundled fallback unless the stored profile is rejected and must be cleared.
    /// </summary>
    Task<HostPairingActionResult> ConnectUsingStoredProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect using configured bundled pairing profiles.
    /// </summary>
    Task<HostPairingActionResult> ConnectUsingBundledProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects using a QR payload, bootstrap document, or full pairing config payload.
    /// </summary>
    Task<HostPairingActionResult> ConnectFromPayloadAsync(
        string payload,
        string? sourceDescription = null,
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears both the stored preferred pairing profile and the runtime cached host profile.
    /// </summary>
    HostPairingActionResult ClearStoredProfiles();
}
