namespace Ansight.Pairing.Models;

/// <summary>
/// Handshake request sent to the host before a live pairing WebSocket session is opened.
/// </summary>
public sealed class ConnectRequest
{
    /// <summary>
    /// Protocol message type identifier.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Handshake protocol version.
    /// </summary>
    public required int Ver { get; set; }

    /// <summary>
    /// Unique identifier of the pairing config being used.
    /// </summary>
    public required string ConfigId { get; set; }

    /// <summary>
    /// One-time token that authorizes the connection attempt.
    /// </summary>
    public required string OneTimeToken { get; set; }

    /// <summary>
    /// App identifier expected by the pairing config.
    /// </summary>
    public required string AppId { get; set; }

    /// <summary>
    /// Human-readable name presented to the host for this client.
    /// </summary>
    public required string ClientName { get; set; }

    /// <summary>
    /// Process-lifetime identifier for the SDK runtime instance.
    /// </summary>
    public string? ProcessSessionId { get; set; }

    /// <summary>
    /// Indicates that the request came from a build-time developer pairing marker rather than a signed pairing config.
    /// </summary>
    public bool DevelopmentPairing { get; set; }
}
