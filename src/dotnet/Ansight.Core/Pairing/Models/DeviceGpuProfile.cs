namespace Ansight.Pairing.Models;

/// <summary>
/// GPU metadata for the connected device.
/// </summary>
public sealed class DeviceGpuProfile
{
    /// <summary>
    /// GPU vendor name.
    /// </summary>
    public string? Vendor { get; set; }

    /// <summary>
    /// GPU model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Renderer string reported by the graphics stack.
    /// </summary>
    public string? Renderer { get; set; }

    /// <summary>
    /// Protocol-defined graphics API code.
    /// </summary>
    public int? ApiCode { get; set; }

    /// <summary>
    /// GPU driver version string.
    /// </summary>
    public string? DriverVersion { get; set; }

    /// <summary>
    /// Approximate dedicated or shared VRAM in megabytes.
    /// </summary>
    public long? VramMb { get; set; }

    /// <summary>
    /// Driver or platform feature level string.
    /// </summary>
    public string? FeatureLevel { get; set; }
}
