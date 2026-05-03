namespace Ansight.Pairing.Models;

/// <summary>
/// Single runtime layer entry within a <see cref="DeviceRuntimeProfile.Stack"/>.
/// </summary>
public sealed class DeviceRuntimeStackEntry
{
    /// <summary>
    /// Protocol-defined runtime code for the layer.
    /// </summary>
    public int? RuntimeCode { get; set; }

    /// <summary>
    /// Runtime layer name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Runtime layer version string.
    /// </summary>
    public string? Version { get; set; }
}
