namespace Ansight.Pairing.Models;

public sealed class DeviceGraphicsProfile
{
    public int? RenderBackendCode { get; set; }
    public int? FpsTarget { get; set; }
    public bool? VsyncEnabled { get; set; }
}
