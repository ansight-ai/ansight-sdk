namespace Ansight.Pairing.Models;

public sealed class DeviceAppProfile
{
    public string Type { get; set; } = "DeviceAppProfile";
    public string Schema { get; set; } = "ansight.device-app-profile.v1";
    public long SentAt { get; set; }
    public int ReasonCode { get; set; } = 1;
    public int ProfileSeq { get; set; } = 1;
    public DeviceProfile? Device { get; set; }
    public DeviceApplicationProfile? App { get; set; }
    public DeviceRuntimeProfile? Runtime { get; set; }
    public DeviceGraphicsProfile? Graphics { get; set; }
    public Dictionary<string, string>? Permissions { get; set; }
    public List<string>? Tags { get; set; }
}
