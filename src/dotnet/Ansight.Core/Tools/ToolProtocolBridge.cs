namespace Ansight.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Protocol adapter that exposes registered tools through the Ansight tool query/call envelope format.
/// </summary>
public sealed class ToolProtocolBridge
{
    /// <summary>
    /// Capability identifier declared on tool protocol envelopes handled by this bridge.
    /// </summary>
    public const string Capability = "tool.exec";

    /// <summary>
    /// Envelope type used by clients to query the visible tool catalog.
    /// </summary>
    public const string QueryType = "tool.query";

    /// <summary>
    /// Envelope type used by the bridge to return a tool catalog.
    /// </summary>
    public const string CatalogType = "tool.catalog";

    /// <summary>
    /// Envelope type used by clients to invoke a tool.
    /// </summary>
    public const string CallType = "tool.call";

    /// <summary>
    /// Envelope type used by the bridge to return a successful tool call result.
    /// </summary>
    public const string ResultType = "tool.result";

    /// <summary>
    /// Envelope type used by the bridge to return a protocol or execution error.
    /// </summary>
    public const string ErrorType = "tool.error";

    private readonly ToolRegistry registry;
    private readonly ToolGuard guard;

    /// <summary>
    /// Creates a protocol bridge over a tool registry and guard policy.
    /// </summary>
    /// <param name="registry">Tool registry that supplies the catalog and execution targets.</param>
    /// <param name="guard">Guard policy that controls discovery and execution.</param>
    public ToolProtocolBridge(ToolRegistry registry, ToolGuard guard)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    /// <summary>
    /// Guard policy currently applied by the bridge.
    /// </summary>
    public ToolGuard Guard => guard;

    /// <summary>
    /// Returns the visible tool catalog after applying the current guard policy.
    /// </summary>
    /// <returns>Visible tool definitions.</returns>
    public IReadOnlyList<ToolDefinition> GetVisibleTools()
        => registry.Where(guard.IsToolVisible).Select(tool => tool.Definition).ToList();

    /// <summary>
    /// Handles a tool-protocol envelope and returns the corresponding catalog, result, or error envelope.
    /// </summary>
    /// <param name="envelope">Incoming tool-protocol envelope.</param>
    /// <param name="cancellationToken">Cancellation token for tool execution.</param>
    /// <returns>Response envelope produced by the bridge.</returns>
    public async Task<ToolProtocolEnvelope> HandleAsync(ToolProtocolEnvelope envelope, CancellationToken cancellationToken = default)
        => await HandleAsync(envelope, cancellationToken, sessionAuthorization: null);

    internal async Task<ToolProtocolEnvelope> HandleAsync(
        ToolProtocolEnvelope envelope,
        CancellationToken cancellationToken,
        Func<ITool, bool>? sessionAuthorization)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return envelope.Type switch
        {
            QueryType => CreateCatalogEnvelope(envelope, sessionAuthorization),
            CallType => await ExecuteEnvelopeAsync(envelope, cancellationToken, sessionAuthorization),
            _ => CreateErrorEnvelope(envelope, "tool_protocol_unknown_type", $"Unsupported tool protocol message type '{envelope.Type}'.", retryable: false)
        };
    }

    /// <summary>
    /// Attempts to parse a JSON tool-protocol envelope.
    /// </summary>
    /// <param name="json">JSON text to parse.</param>
    /// <param name="envelope">Parsed envelope when parsing succeeds.</param>
    /// <param name="error">Parsing or validation error message when parsing fails.</param>
    /// <returns><see langword="true"/> when the JSON parsed into a valid envelope; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>
    /// Serializes a tool-protocol envelope to JSON.
    /// </summary>
    /// <param name="envelope">Envelope to serialize.</param>
    /// <param name="indented"><see langword="true"/> to format the JSON with indentation.</param>
    /// <returns>Serialized envelope JSON.</returns>
    public string SerializeEnvelope(ToolProtocolEnvelope envelope, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, indented ? Pairing.PairingJson.Pretty : Pairing.PairingJson.Compact);
    }

    private ToolProtocolEnvelope CreateCatalogEnvelope(
        ToolProtocolEnvelope request,
        Func<ITool, bool>? sessionAuthorization)
    {
        if (!guard.DiscoveryEnabled)
        {
            return CreateErrorEnvelope(request, "tool_discovery_disabled", "Tool discovery is disabled by the current guard policy.", retryable: false);
        }

        var tools = new JsonArray();
        foreach (var tool in registry)
        {
            if (!guard.IsToolVisible(tool) || sessionAuthorization?.Invoke(tool) == false)
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

    private async Task<ToolProtocolEnvelope> ExecuteEnvelopeAsync(
        ToolProtocolEnvelope request,
        CancellationToken cancellationToken,
        Func<ITool, bool>? sessionAuthorization)
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

        if (sessionAuthorization?.Invoke(tool) == false)
        {
            return CreateErrorEnvelope(
                request,
                "tool_grant_denied",
                "The authenticated session grant does not permit this tool.",
                retryable: false);
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

        arguments[ToolExecutionArgumentNames.RequestId] = request.Id;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            arguments[ToolExecutionArgumentNames.SessionId] = request.SessionId!;
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

            var resultPayload = new JsonObject
            {
                ["toolId"] = toolId,
                ["success"] = true,
                ["message"] = result.Message,
                ["result"] = result.Payload
            };

            return new ToolProtocolEnvelope
            {
                Type = ResultType,
                Id = CreateResponseId(request.Id),
                ReplyTo = request.Id,
                SessionId = request.SessionId,
                Capability = Capability,
                SentAt = DateTimeOffset.UtcNow,
                Payload = ToolProtocolPayloadEncoding.EncodeIfBeneficial(resultPayload, Pairing.PairingJson.Compact)
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
        var json = new JsonObject
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

        if (definition.Security is { IsSpecified: true } security)
        {
            json["security"] = security.ToJson();
        }

        return json;
    }
}
