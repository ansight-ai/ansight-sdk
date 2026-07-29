using Ansight;

namespace Ansight.Pairing.Models;

/// <summary>
/// Optional inputs that customize how a pairing session is opened.
/// </summary>
public sealed class PairingConnectionOptions
{
    /// <summary>
    /// Optional manual host-address override for advanced recovery scenarios.
    /// When omitted, Ansight uses the address embedded in the pairing config.
    /// </summary>
    public string? HostAddressOverride { get; set; }

    /// <summary>
    /// Optional UDP discovery port to use for the initial pairing bootstrap.
    /// When omitted, Ansight prefers a discovery hint port, then any legacy config port, then the default protocol port.
    /// </summary>
    public int? DiscoveryPort { get; set; }

    /// <summary>
    /// Allows the pairing connection to be attempted while the active network path is cellular.
    /// Disabled by default.
    /// </summary>
    public bool AllowCellularConnections { get; set; }

    /// <summary>
    /// Optional app profile values to add to or override the automatically collected baseline profile.
    /// </summary>
    public DeviceAppProfile? DeviceAppProfile { get; set; }

    /// <summary>
    /// Optional custom grouped properties to send when the live pairing session opens.
    /// </summary>
    public SessionCustomProperties? CustomProperties { get; set; }

    internal PairingConnectionOptions Clone()
        => new()
        {
            HostAddressOverride = HostAddressOverride,
            DiscoveryPort = DiscoveryPort,
            AllowCellularConnections = AllowCellularConnections,
            DeviceAppProfile = DeviceAppProfile,
            CustomProperties = CustomProperties?.Clone()
        };
}
