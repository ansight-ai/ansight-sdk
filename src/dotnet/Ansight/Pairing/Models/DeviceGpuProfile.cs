namespace Ansight.Pairing.Models;

public sealed class DeviceGpuProfile
{
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? Renderer { get; set; }
    public int? ApiCode { get; set; }
    public string? DriverVersion { get; set; }
    public long? VramMb { get; set; }
    public string? FeatureLevel { get; set; }
}
