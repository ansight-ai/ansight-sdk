namespace Ansight.Pairing.Models;

/// <summary>
/// Parsed pairing payload that exposes the effective config plus any discovery or bootstrap metadata that accompanied it.
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
    /// Original signed config used as the trust anchor when <see cref="Config"/> contains connection-hint overrides.
    /// </summary>
    public PairingConfig? TrustAnchorConfig { get; init; }

    /// <summary>
    /// Optional connection hint that supplied transient overrides such as token or expiry.
    /// </summary>
    public PairingConnectionHint? ConnectionHint { get; init; }
}
