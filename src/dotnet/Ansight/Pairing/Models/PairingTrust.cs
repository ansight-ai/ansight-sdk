namespace Ansight.Pairing.Models;

public sealed class PairingTrust
{
    public required string Mode { get; set; }
    public required bool RequireTokenOnFirstPair { get; set; }
    public required bool AllowLanDiscovery { get; set; }
}
