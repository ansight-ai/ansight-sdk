namespace Ansight.Pairing.Models;

public sealed class DeviceBatteryProfile
{
    public int? LevelPct { get; set; }
    public int? StateCode { get; set; }
    public int? HealthCode { get; set; }
    public double? TemperatureC { get; set; }
}
