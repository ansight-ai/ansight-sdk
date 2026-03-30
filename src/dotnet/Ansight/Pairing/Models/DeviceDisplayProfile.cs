namespace Ansight.Pairing.Models;

/// <summary>
/// Display metadata for the connected device.
/// </summary>
public sealed class DeviceDisplayProfile
{
    /// <summary>
    /// Display width in physical pixels.
    /// </summary>
    public int? WidthPx { get; set; }

    /// <summary>
    /// Display height in physical pixels.
    /// </summary>
    public int? HeightPx { get; set; }

    /// <summary>
    /// Display density in DPI.
    /// </summary>
    public int? DensityDpi { get; set; }

    /// <summary>
    /// Maximum or active display refresh rate in hertz.
    /// </summary>
    public double? RefreshRateHz { get; set; }

    /// <summary>
    /// Indicates whether the display supports HDR output.
    /// </summary>
    public bool? HdrSupported { get; set; }
}
