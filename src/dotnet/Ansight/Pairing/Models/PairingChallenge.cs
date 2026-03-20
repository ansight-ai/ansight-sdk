namespace Ansight.Pairing.Models;

public sealed class PairingChallenge
{
    public required string Alg { get; set; }
    public required string ChallengePubKey { get; set; }
    public required bool RequireProofOnFirstPair { get; set; }
}
