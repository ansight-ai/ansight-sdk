namespace Ansight.Pairing.Models;

public sealed class ParsedPairingDocument
{
    public required PairingConfig Config { get; init; }
    public PairingDiscoveryHint? DiscoveryHint { get; init; }
    public PairingConfig? TrustAnchorConfig { get; init; }
    public PairingConnectionHint? ConnectionHint { get; init; }
}
