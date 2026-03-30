namespace Ansight.Pairing;

/// <summary>
/// Canonical machine-readable failure and rejection codes surfaced by the Ansight pairing APIs.
/// </summary>
public static class PairingFailureCodes
{
    /// <summary>
    /// A manual connection attempt did not supply a host address.
    /// </summary>
    public const string HostAddressRequired = "HostAddressRequired";

    /// <summary>
    /// The host requires the client to pair again before a session can be opened.
    /// </summary>
    public const string PairingRequired = "PairingRequired";

    /// <summary>
    /// The one-time pairing token supplied by the client is invalid.
    /// </summary>
    public const string PairingTokenInvalid = "PairingTokenInvalid";

    /// <summary>
    /// The one-time pairing token supplied by the client has expired.
    /// </summary>
    public const string PairingTokenExpired = "PairingTokenExpired";

    /// <summary>
    /// The client proof submitted for the pairing challenge is invalid.
    /// </summary>
    public const string PairingProofInvalid = "PairingProofInvalid";

    /// <summary>
    /// The UDP bootstrap handshake failed before the client could resolve a WebSocket endpoint.
    /// </summary>
    public const string UdpBootstrapFailed = "UdpBootstrapFailed";

    /// <summary>
    /// The UDP bootstrap handshake timed out before the client could resolve a WebSocket endpoint.
    /// </summary>
    public const string UdpBootstrapTimeout = "UdpBootstrapTimeout";

    /// <summary>
    /// The host accepted the request but did not return a usable WebSocket handoff.
    /// </summary>
    public const string WebSocketHandoffUnavailable = "WebSocketHandoffUnavailable";

    /// <summary>
    /// The client could not reach the advertised WebSocket endpoint.
    /// </summary>
    public const string WebSocketEndpointUnreachable = "WebSocketEndpointUnreachable";

    /// <summary>
    /// The WebSocket handshake failed after reaching the advertised endpoint.
    /// </summary>
    public const string WebSocketHandshakeFailed = "WebSocketHandshakeFailed";
}
