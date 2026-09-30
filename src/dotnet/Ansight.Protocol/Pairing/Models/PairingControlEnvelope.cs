using System.Text.Json.Nodes;

namespace Ansight.Pairing.Models;

/// <summary>
/// Request/response envelope used for correlated control messages on the live pairing WebSocket session.
/// </summary>
public sealed class PairingControlEnvelope
{
    public const string RequestType = "CONTROL_REQ";
    public const string ResponseType = "CONTROL_RESP";

    /// <summary>
    /// Message type identifier.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Request identifier for outbound requests.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Request identifier that this response answers.
    /// </summary>
    public string? ReplyTo { get; set; }

    /// <summary>
    /// Logical action being requested or answered.
    /// </summary>
    public required string Action { get; set; }

    /// <summary>
    /// Optional structured payload for the action.
    /// </summary>
    public JsonObject? Payload { get; set; }

    /// <summary>
    /// Indicates whether the request succeeded.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string? Message { get; set; }
}
