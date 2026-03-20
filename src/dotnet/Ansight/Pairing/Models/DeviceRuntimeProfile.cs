namespace Ansight.Pairing.Models;

public sealed class DeviceRuntimeProfile
{
    public int? Primary { get; set; }
    public string? PrimaryVersion { get; set; }
    public DeviceRuntimeEngineProfile? Engine { get; set; }
    public List<DeviceRuntimeStackEntry>? Stack { get; set; }
    public bool? AotEnabled { get; set; }
    public bool? JitEnabled { get; set; }
}
