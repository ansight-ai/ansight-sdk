namespace Ansight.Pairing.Models;

/// <summary>
/// Graphics configuration metadata for the connected app runtime.
/// </summary>
public sealed class DeviceGraphicsProfile
{
    /// <summary>
    /// Protocol-defined render backend code.
    /// </summary>
    public int? RenderBackendCode { get; set; }

    /// <summary>
    /// Target frame rate configured by the app or platform, when known.
    /// </summary>
    public int? FpsTarget { get; set; }

    /// <summary>
    /// Indicates whether vertical sync is enabled.
    /// </summary>
    public bool? VsyncEnabled { get; set; }
}
