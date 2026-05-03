namespace Ansight.Pairing.Models;

/// <summary>
/// Trust-policy metadata associated with a host-issued pairing config.
/// </summary>
public sealed class PairingTrust
{
    /// <summary>
    /// Host-defined trust mode identifier.
    /// </summary>
    public required string Mode { get; set; }

    /// <summary>
    /// Indicates whether the first successful pair must also present the one-time token.
    /// </summary>
    public required bool RequireTokenOnFirstPair { get; set; }

    /// <summary>
    /// Indicates whether LAN discovery is allowed for this config.
    /// </summary>
    public required bool AllowLanDiscovery { get; set; }
}
