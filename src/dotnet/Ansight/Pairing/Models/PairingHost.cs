namespace Ansight.Pairing.Models;

public sealed class PairingHost
{
    public string? HostId { get; set; }
    public string? HostName { get; set; }
    public int DiscoveryPort { get; set; } = PairingProtocolDefaults.DiscoveryPort;
    public required string HostPubKey { get; set; }
    public required string HostPubKeyFingerprint { get; set; }
}
