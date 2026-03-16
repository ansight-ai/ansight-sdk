namespace Ansight.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class ToolProtocolBridge
{
    public const string Capability = "tool.exec";
    public const string QueryType = "tool.query";
    public const string CatalogType = "tool.catalog";
    public const string CallType = "tool.call";
    public const string ResultType = "tool.result";
    public const string ErrorType = "tool.error";

    private readonly ToolRegistry registry;
    private readonly ToolGuard guard;

    public ToolProtocolBridge(ToolRegistry registry, ToolGuard guard)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public ToolGuard Guard => guard;

    public IReadOnlyList<ToolDefinition> GetVisibleTools()
        => registry.Where(guard.IsToolVisible).Select(tool => tool.Definition).ToList();

    public async Task<ToolProtocolEnvelope> HandleAsync(ToolProtocolEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return envelope.Type switch
        {
            QueryType => CreateCatalogEnvelope(envelope),
            CallType => await ExecuteEnvelopeAsync(envelope, cancellationToken),
            _ => CreateErrorEnvelope(envelope, "tool_protocol_unknown_type", $"Unsupported tool protocol message type '{envelope.Type}'.", retryable: false)
        };
    }

    public bool TryParseEnvelope(string json, out ToolProtocolEnvelope? envelope, out string error)
    {
        envelope = null;
        error = string.Empty;

        try
        {
            envelope = JsonSerializer.Deserialize<ToolProtocolEnvelope>(json, Pairing.PairingJson.Compact);
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Type) || string.IsNullOrWhiteSpace(envelope.Id))
            {
                error = "Tool protocol envelope must include a non-empty type and id.";
                envelope = null;
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

    public string SerializeEnvelope(ToolProtocolEnvelope envelope, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, indented ? Pairing.PairingJson.Pretty : Pairing.PairingJson.Compact);
    }

    private ToolProtocolEnvelope CreateCatalogEnvelope(ToolProtocolEnvelope request)
    {
        if (!guard.DiscoveryEnabled)
        {
            return CreateErrorEnvelope(request, "tool_discovery_disabled", "Tool discovery is disabled by the current guard policy.", retryable: false);
        }

        var tools = new JsonArray();
        foreach (var tool in registry)
        {
            if (!guard.IsToolVisible(tool))
            {
                continue;
            }

            tools.Add(ToJson(tool.Definition));
        }

        return new ToolProtocolEnvelope
        {
            Type = CatalogType,
            Id = CreateResponseId(request.Id),
            ReplyTo = request.Id,
            SessionId = request.SessionId,
            Capability = Capability,
            SentAt = DateTimeOffset.UtcNow,
            Payload = new JsonObject
            {
                ["guard"] = guard.ToJson(),
                ["tools"] = tools,
                ["count"] = tools.Count
            }
        };
    }

    private async Task<ToolProtocolEnvelope> ExecuteEnvelopeAsync(ToolProtocolEnvelope request, CancellationToken cancellationToken)
    {
        if (request.Payload is not JsonObject payload)
        {
            return CreateErrorEnvelope(request, "tool_call_payload_invalid", "Tool call payload must be a JSON object.", retryable: false);
        }

        var toolId = payload["toolId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return CreateErrorEnvelope(request, "tool_call_missing_id", "Tool call payload must include 'toolId'.", retryable: false);
        }

        if (!registry.TryGet(toolId, out var tool) || tool == null)
        {
            return CreateErrorEnvelope(request, "tool_not_found", $"Tool '{toolId}' is not registered.", retryable: false);
        }

        if (!guard.CanExecute(tool, out var denialReason))
        {
            return CreateErrorEnvelope(request, "tool_execution_denied", denialReason ?? "Tool execution is denied by the current guard policy.", retryable: false);
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload["arguments"] is JsonObject argumentObject)
        {
            foreach (var property in argumentObject)
            {
                if (property.Value == null)
                {
                    continue;
                }

                arguments[property.Key] = property.Value.ToJsonString();
                if (property.Value is JsonValue value)
                {
                    arguments[property.Key] = value.ToString();
                }
            }
        }

        try
        {
            var result = await tool.Execute(arguments);
            if (!result.IsSuccess)
            {
                return CreateErrorEnvelope(
                    request,
                    result.ErrorCode ?? "tool_execution_failed",
                    result.Message ?? $"Tool '{toolId}' failed.",
                    retryable: false,
                    details: result.Payload);
            }

            return new ToolProtocolEnvelope
            {
                Type = ResultType,
                Id = CreateResponseId(request.Id),
                ReplyTo = request.Id,
                SessionId = request.SessionId,
                Capability = Capability,
                SentAt = DateTimeOffset.UtcNow,
                Payload = new JsonObject
                {
                    ["toolId"] = toolId,
                    ["success"] = true,
                    ["message"] = result.Message,
                    ["result"] = result.Payload
                }
            };
        }
        catch (Exception exception)
        {
            return CreateErrorEnvelope(request, "tool_execution_exception", exception.Message, retryable: false);
        }
    }

    private ToolProtocolEnvelope CreateErrorEnvelope(
        ToolProtocolEnvelope request,
        string code,
        string message,
        bool retryable,
        JsonNode? details = null)
    {
        return new ToolProtocolEnvelope
        {
            Type = ErrorType,
            Id = CreateResponseId(request.Id),
            ReplyTo = request.Id,
            SessionId = request.SessionId,
            Capability = Capability,
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

    private static string CreateResponseId(string requestId) => $"{requestId}.response";

    private static JsonObject ToJson(ToolDefinition definition)
    {
        return new JsonObject
        {
            ["id"] = definition.Id,
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["category"] = definition.Category,
            ["scope"] = definition.Scope.ToString(),
            ["keywords"] = definition.Keywords,
            ["argumentsSchema"] = definition.ArgumentsSchema.ToJson(),
            ["resultSchema"] = definition.ResultSchema.ToJson()
        };
    }
}
