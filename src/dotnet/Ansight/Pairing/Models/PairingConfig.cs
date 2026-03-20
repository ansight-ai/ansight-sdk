namespace Ansight.Pairing.Models;

public sealed class PairingConfig
{
    public required string Schema { get; set; }
    public required string ConfigId { get; set; }
    public required string AppId { get; set; }
    public required string AppName { get; set; }
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string OneTimeToken { get; set; }
    public required PairingHost Host { get; set; }
    public required PairingChallenge Challenge { get; set; }
    public required PairingTrust Trust { get; set; }
    public required string Signature { get; set; }
}
