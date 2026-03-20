namespace Ansight.Pairing.Models;

public sealed class DeviceNetworkProfile
{
    public int? TransportCode { get; set; }
    public bool? Metered { get; set; }
    public string? EffectiveType { get; set; }
    public int? RttMs { get; set; }
    public int? DownKbps { get; set; }
}
