namespace Ansight.Pairing.Models;

/// <summary>
/// Parsed pairing payload that exposes the effective config plus any discovery metadata that accompanied it.
/// </summary>
public sealed class ParsedPairingDocument
{
    /// <summary>
    /// Effective pairing config to use for validation and connection.
    /// </summary>
    public required PairingConfig Config { get; init; }

    /// <summary>
    /// Optional discovery metadata captured from the payload.
    /// </summary>
    public PairingDiscoveryHint? DiscoveryHint { get; init; }

    /// <summary>
    /// Indicates that this document came from the build-time developer pairing marker.
    /// Developer pairing intentionally skips signed pairing config validation and is only
    /// intended for local development builds.
    /// </summary>
    public bool IsDevelopmentPairing { get; init; }
}
