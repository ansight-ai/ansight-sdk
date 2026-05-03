namespace Ansight.Pairing.Models;

/// <summary>
/// Runtime-stack metadata for the connected app.
/// </summary>
public sealed class DeviceRuntimeProfile
{
    /// <summary>
    /// Protocol-defined primary runtime code.
    /// </summary>
    public int? Primary { get; set; }

    /// <summary>
    /// Version of the primary runtime layer.
    /// </summary>
    public string? PrimaryVersion { get; set; }

    /// <summary>
    /// Engine metadata for the active runtime.
    /// </summary>
    public DeviceRuntimeEngineProfile? Engine { get; set; }

    /// <summary>
    /// Full runtime stack from app runtime through platform runtime layers.
    /// </summary>
    public List<DeviceRuntimeStackEntry>? Stack { get; set; }

    /// <summary>
    /// Indicates whether ahead-of-time compilation is enabled.
    /// </summary>
    public bool? AotEnabled { get; set; }

    /// <summary>
    /// Indicates whether JIT compilation is enabled.
    /// </summary>
    public bool? JitEnabled { get; set; }
}
