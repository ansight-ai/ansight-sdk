namespace Ansight.Pairing;

/// <summary>
/// Default transport values used by the Ansight pairing protocol.
/// </summary>
public static class PairingProtocolDefaults
{
    /// <summary>
    /// Default UDP discovery port exposed by the host.
    /// </summary>
    public const int DiscoveryPort = 45123;

    /// <summary>
    /// Alternate UDP discovery port exposed by a source-built developer Studio.
    /// </summary>
    public const int DeveloperDiscoveryPort = 46123;

    /// <summary>
    /// Default WebSocket port used for live pairing sessions.
    /// </summary>
    public const int WebSocketPort = 45124;

    /// <summary>
    /// Default WebSocket path used for live pairing sessions.
    /// </summary>
    public const string WebSocketPath = "/ws";
}
