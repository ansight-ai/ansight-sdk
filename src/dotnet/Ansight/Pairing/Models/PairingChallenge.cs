namespace Ansight.Pairing.Models;

/// <summary>
/// Describes the host challenge material required to authenticate the pairing attempt.
/// </summary>
public sealed class PairingChallenge
{
    /// <summary>
    /// Algorithm identifier used for the challenge/proof flow.
    /// </summary>
    public required string Alg { get; set; }

    /// <summary>
    /// Challenge public key or equivalent challenge material exposed by the host.
    /// </summary>
    public required string ChallengePubKey { get; set; }

    /// <summary>
    /// Indicates whether proof is required on the first successful pair.
    /// </summary>
    public required bool RequireProofOnFirstPair { get; set; }
}
