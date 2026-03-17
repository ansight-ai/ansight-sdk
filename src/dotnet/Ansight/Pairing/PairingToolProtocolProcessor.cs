using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ansight.Tools;

namespace Ansight.Pairing;

internal static class PairingToolProtocolProcessor
{
    public static async Task<ToolProtocolProcessResult> ProcessAsync(string messageJson, CancellationToken cancellationToken)
    {
        if (!Runtime.IsInitialized)
        {
            return ToolProtocolProcessResult.FromFailure("Runtime must be initialized before processing tool protocol messages.");
        }

        var bridge = Runtime.ToolBridge;
        if (!bridge.TryParseEnvelope(messageJson, out var envelope, out var error))
        {
            return ToolProtocolProcessResult.FromFailure(error);
        }

        var response = await bridge.HandleAsync(envelope!, cancellationToken);
        return ToolProtocolProcessResult.FromSuccess(bridge.SerializeEnvelope(response));
    }

    public static async Task<bool> TryHandleIncomingMessageAsync(
        ClientWebSocket webSocket,
        string messageJson,
        Func<ClientWebSocket, string, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken)
    {
        if (!TryParseToolProtocolRequest(messageJson, out var envelope, out var error, out var isToolProtocolMessage))
        {
            if (!isToolProtocolMessage)
            {
                return false;
            }

            var invalidRequest = CreateToolProtocolErrorEnvelope(
                requestId: null,
                sessionId: null,
                replyTo: null,
                code: "tool_protocol_invalid_request",
                message: error,
                retryable: false);

            await sendAsync(webSocket, SerializeToolEnvelope(invalidRequest), cancellationToken);
            return true;
        }

        ToolProtocolEnvelope response;
        if (!Runtime.IsInitialized)
        {
            response = CreateToolProtocolErrorEnvelope(
                envelope!.Id,
                envelope.SessionId,
                envelope.Id,
                code: "tool_runtime_not_initialized",
                message: "Runtime must be initialized before remote tools can be queried or executed.",
                retryable: false);
        }
        else
        {
            response = await Runtime.ToolBridge.HandleAsync(envelope!, cancellationToken);
        }

        await sendAsync(webSocket, SerializeToolEnvelope(response), cancellationToken);
        return true;
    }

    private static bool TryParseToolProtocolRequest(
        string messageJson,
        out ToolProtocolEnvelope? envelope,
        out string error,
        out bool isToolProtocolMessage)
    {
        envelope = null;
        error = string.Empty;
        isToolProtocolMessage = false;

        try
        {
            using var document = JsonDocument.Parse(messageJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!document.RootElement.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString();
            if (!string.Equals(type, ToolProtocolBridge.QueryType, StringComparison.Ordinal) &&
                !string.Equals(type, ToolProtocolBridge.CallType, StringComparison.Ordinal))
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("capability", out var capabilityElement) &&
                capabilityElement.ValueKind == JsonValueKind.String)
            {
                var capability = capabilityElement.GetString();
                if (!string.IsNullOrWhiteSpace(capability) &&
                    !string.Equals(capability, ToolProtocolBridge.Capability, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            isToolProtocolMessage = true;
            envelope = JsonSerializer.Deserialize<ToolProtocolEnvelope>(messageJson, PairingJson.Compact);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Id))
            {
                error = "Tool protocol envelope must include a non-empty id.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"Failed to parse tool protocol envelope: {exception.Message}";
            return false;
        }
    }

    private static ToolProtocolEnvelope CreateToolProtocolErrorEnvelope(
        string? requestId,
        string? sessionId,
        string? replyTo,
        string code,
        string message,
        bool retryable,
        JsonNode? details = null)
    {
        return new ToolProtocolEnvelope
        {
            Type = ToolProtocolBridge.ErrorType,
            Id = string.IsNullOrWhiteSpace(requestId)
                ? $"tool.error.{Guid.NewGuid():N}"
                : $"{requestId}.response",
            ReplyTo = replyTo,
            SessionId = sessionId,
            Capability = ToolProtocolBridge.Capability,
            SentAt = DateTimeOffset.UtcNow,
            Payload = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["retryable"] = retryable,
                ["details"] = details
            }
        };
    }

    private static string SerializeToolEnvelope(ToolProtocolEnvelope envelope)
        => JsonSerializer.Serialize(envelope, PairingJson.Compact);
}
