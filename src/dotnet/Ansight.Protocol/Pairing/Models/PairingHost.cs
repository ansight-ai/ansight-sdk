namespace Ansight.Pairing.Models;

/// <summary>
/// Host identity and transport metadata carried by an enrollment invite.
/// </summary>
public sealed class PairingHost
{
    /// <summary>
    /// Stable identifier for the host, when one is provided.
    /// </summary>
    public string? HostId { get; set; }

    /// <summary>
    /// Human-readable host name, when one is provided.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// UDP discovery port advertised by the host.
    /// </summary>
    public int DiscoveryPort { get; set; } = PairingProtocolDefaults.DiscoveryPort;

}
