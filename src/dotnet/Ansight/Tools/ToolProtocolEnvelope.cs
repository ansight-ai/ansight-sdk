namespace Ansight.Tools;

using System.Text.Json.Nodes;

public sealed class ToolProtocolEnvelope
{
    public required string Type { get; init; }

    public required string Id { get; init; }

    public string? ReplyTo { get; init; }

    public string? SessionId { get; init; }

    public DateTimeOffset SentAt { get; init; } = DateTimeOffset.UtcNow;

    public string Capability { get; init; } = ToolProtocolBridge.Capability;

    public JsonNode? Payload { get; init; }
}
