namespace Ansight.Pairing.Models;

/// <summary>
/// Network metadata for the connected device.
/// </summary>
public sealed class DeviceNetworkProfile
{
    /// <summary>
    /// Protocol-defined transport code for the active network path.
    /// </summary>
    public int? TransportCode { get; set; }

    /// <summary>
    /// Indicates whether the current network is metered.
    /// </summary>
    public bool? Metered { get; set; }

    /// <summary>
    /// Effective network type label, such as Wi-Fi or LTE.
    /// </summary>
    public string? EffectiveType { get; set; }

    /// <summary>
    /// Estimated round-trip latency in milliseconds.
    /// </summary>
    public int? RttMs { get; set; }

    /// <summary>
    /// Estimated downstream bandwidth in kilobits per second.
    /// </summary>
    public int? DownKbps { get; set; }
}
