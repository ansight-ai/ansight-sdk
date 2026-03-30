namespace Ansight.Pairing.Models;

/// <summary>
/// Handshake response returned by the host after a connection attempt.
/// </summary>
public sealed class ConnectResponse
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
    /// Indicates whether the host accepted the connection attempt.
    /// </summary>
    public required bool Accepted { get; set; }

    /// <summary>
    /// Machine-readable reason code returned by the host.
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Optional human-readable explanation for the host decision.
    /// </summary>
    public string? ReasonMessage { get; set; }

    /// <summary>
    /// Stable identifier of the responding host.
    /// </summary>
    public required string HostId { get; set; }

    /// <summary>
    /// Human-readable name of the responding host.
    /// </summary>
    public required string HostName { get; set; }

    /// <summary>
    /// Human-readable handshake status message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// WebSocket port to use for the live session when the connection is accepted.
    /// </summary>
    public int? WebSocketPort { get; set; }

    /// <summary>
    /// WebSocket path to use for the live session when the connection is accepted.
    /// </summary>
    public string? WebSocketPath { get; set; }

    /// <summary>
    /// Optional host-issued token required by the WebSocket handoff.
    /// </summary>
    public string? WebSocketToken { get; set; }
}
