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

}
