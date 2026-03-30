namespace Ansight.Pairing.Models;

/// <summary>
/// Battery-related metadata for the connected device.
/// </summary>
public sealed class DeviceBatteryProfile
{
    /// <summary>
    /// Battery charge level as a percentage from 0 to 100.
    /// </summary>
    public int? LevelPct { get; set; }

    /// <summary>
    /// Protocol-defined battery state code.
    /// </summary>
    public int? StateCode { get; set; }

    /// <summary>
    /// Protocol-defined battery health code.
    /// </summary>
    public int? HealthCode { get; set; }

    /// <summary>
    /// Battery temperature in degrees Celsius.
    /// </summary>
    public double? TemperatureC { get; set; }
}
