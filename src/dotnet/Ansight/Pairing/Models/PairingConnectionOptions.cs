namespace Ansight.Pairing.Models;

/// <summary>
/// Optional inputs that customize how a pairing session is opened.
/// </summary>
public sealed class PairingConnectionOptions
{
    /// <summary>
    /// Selects whether the client should use configured discovery data or a manually supplied host address.
    /// </summary>
    public PairingDiscoveryMode DiscoveryMode { get; set; } = PairingDiscoveryMode.ConfiguredHint;

    /// <summary>
    /// Manual host address to use when <see cref="DiscoveryMode"/> is <see cref="PairingDiscoveryMode.BasicManual"/>.
    /// </summary>
    public string? ManualHostAddress { get; set; }

    /// <summary>
    /// Optional UDP discovery port to use for the initial pairing bootstrap.
    /// When omitted, Ansight prefers a discovery hint port, then any legacy config port, then the default protocol port.
    /// </summary>
    public int? DiscoveryPort { get; set; }

    /// <summary>
    /// Optional app profile values to add to or override the automatically collected baseline profile.
    /// </summary>
    public DeviceAppProfile? DeviceAppProfile { get; set; }
}
