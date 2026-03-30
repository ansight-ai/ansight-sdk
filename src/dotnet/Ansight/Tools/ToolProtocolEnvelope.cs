namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Envelope used by the Ansight tool protocol for discovery and execution messages.
/// </summary>
public sealed class ToolProtocolEnvelope
{
    /// <summary>
    /// Protocol message type such as <c>tool.query</c>, <c>tool.call</c>, or <c>tool.result</c>.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Stable identifier for this envelope.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Optional id of the request envelope that this envelope replies to.
    /// </summary>
    public string? ReplyTo { get; init; }

    /// <summary>
    /// Optional session identifier associated with the envelope.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Time at which the envelope was created.
    /// </summary>
    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Capability identifier associated with the envelope.
    /// </summary>
    public string Capability { get; init; } = ToolProtocolBridge.Capability;

    /// <summary>
    /// Arbitrary JSON payload carried by the envelope.
    /// </summary>
    public JsonNode? Payload { get; init; }
}
