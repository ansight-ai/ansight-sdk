namespace Ansight.Pairing.Models;

public sealed class DeviceDisplayProfile
{
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public int? DensityDpi { get; set; }
    public double? RefreshRateHz { get; set; }
    public bool? HdrSupported { get; set; }
}
