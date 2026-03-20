namespace Ansight.Pairing.Models;

public sealed class PairingConnectionOptions
{
    public PairingDiscoveryMode DiscoveryMode { get; set; } = PairingDiscoveryMode.ConfiguredHint;
    public string? ManualHostAddress { get; set; }
    public DeviceAppProfile? DeviceAppProfile { get; set; }
}
