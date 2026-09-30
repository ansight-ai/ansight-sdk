namespace Ansight.Pairing.Models;

/// <summary>
/// Thermal-state metadata for the connected device.
/// </summary>
public sealed class DeviceThermalProfile
{
    /// <summary>
    /// Protocol-defined thermal status code.
    /// </summary>
    public int? StatusCode { get; set; }
}
