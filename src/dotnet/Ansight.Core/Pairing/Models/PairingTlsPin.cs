namespace Ansight.Pairing.Models;

/// <summary>
/// Time-bounded TLS SubjectPublicKeyInfo pin authorized by a protocol-v2 config.
/// </summary>
public sealed class PairingTlsPin
{
    public required string TlsSpkiSha256 { get; set; }

    public required string NotBefore { get; set; }

    public required string NotAfter { get; set; }
}
