namespace Ansight.Pairing.Models;

/// <summary>
/// Controls how a host address is chosen when opening a pairing session.
/// </summary>
public enum PairingDiscoveryMode
{
    /// <summary>
    /// Use the host address from the pairing document's discovery hint.
    /// </summary>
    ConfiguredHint = 0,

    /// <summary>
    /// Use a manually supplied host address.
    /// </summary>
    BasicManual = 1
}
