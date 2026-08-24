namespace Ansight.Tools;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Exposes registered tools through the versioned Ansight query, call, and batch protocol.
/// </summary>
public sealed class ToolProtocolBridge
{
    public const string Capability = "tool.exec";
    public const string QueryType = "tool.query";
    public const string CatalogType = "tool.catalog";
    public const string CallType = "tool.call";
    public const string BatchType = "tool.batch";
    public const string ResultType = "tool.result";
    public const string BatchResultType = "tool.batch.result";
    public const string ErrorType = "tool.error";
    public const string CatalogSchema = "ansight.tool-catalog.v2";

    private const int MaximumBatchSize = 32;
    private const int MaximumEvidenceDelayMilliseconds = 2_000;
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

    public async Task<ToolProtocolEnvelope> HandleAsync(
        ToolProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope.Type switch
        {
            QueryType => await CreateCatalogEnvelopeAsync(envelope),
            CallType => await ExecuteEnvelopeAsync(envelope, cancellationToken),
            BatchType => await ExecuteBatchEnvelopeAsync(envelope, cancellationToken),
            _ => CreateErrorEnvelope(
                envelope,
                "tool_protocol_unknown_type",
                $"Unsupported tool protocol message type '{envelope.Type}'.",
                false)
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
        return JsonSerializer.Serialize(
            envelope,
            indented ? Pairing.PairingJson.Pretty : Pairing.PairingJson.Compact);
    }

    private async Task<ToolProtocolEnvelope> CreateCatalogEnvelopeAsync(ToolProtocolEnvelope request)
    {
        if (!guard.DiscoveryEnabled)
        {
            return CreateErrorEnvelope(
                request,
                "tool_discovery_disabled",
                "Tool discovery is disabled by the current guard policy.",
                false);
        }

        var visibleTools = registry.Where(guard.IsToolVisible).ToList();
        var revision = ComputeCatalogRevision(visibleTools);
        var requestedRevision = (request.Payload as JsonObject)?["ifRevision"]?.GetValue<string>();
        var unchanged = string.Equals(requestedRevision, revision, StringComparison.Ordinal);
        var tools = new JsonArray();
        if (!unchanged)
        {
            foreach (var tool in visibleTools)
            {
                var availability = await tool.GetAvailabilityAsync(
                    new ToolAvailabilityContext(request.SessionId, request.Id));
                tools.Add(ToJson(tool, availability));
            }
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
                ["schema"] = CatalogSchema,
                ["revision"] = revision,
                ["catalogHash"] = revision,
                ["unchanged"] = unchanged,
                ["manifest"] = CreateCapabilityManifest(visibleTools, revision).ToJson(),
                ["guard"] = guard.ToJson(),
                ["tools"] = tools,
                ["count"] = visibleTools.Count
            }
        };
    }

    private async Task<ToolProtocolEnvelope> ExecuteEnvelopeAsync(
        ToolProtocolEnvelope request,
        CancellationToken cancellationToken)
    {
        if (request.Payload is not JsonObject payload)
        {
            return CreateErrorEnvelope(
                request,
                "tool_call_payload_invalid",
                "Tool call payload must be a JSON object.",
                false);
        }

        var outcome = await ExecuteCallAsync(request, payload, cancellationToken);
        return outcome.IsSuccess
            ? CreateResultEnvelope(request, ResultType, outcome.ToJson())
            : CreateErrorEnvelope(
                request,
                outcome.ErrorCode ?? "tool_execution_failed",
                outcome.Message ?? "Tool execution failed.",
                outcome.Retryable,
                outcome.Payload);
    }

    private async Task<ToolProtocolEnvelope> ExecuteBatchEnvelopeAsync(
        ToolProtocolEnvelope request,
        CancellationToken cancellationToken)
    {
        if (request.Payload is not JsonObject payload || payload["calls"] is not JsonArray calls)
        {
            return CreateErrorEnvelope(
                request,
                "tool_batch_payload_invalid",
                "Tool batch payload must contain a 'calls' array.",
                false);
        }

        if (calls.Count == 0 || calls.Count > MaximumBatchSize)
        {
            return CreateErrorEnvelope(
                request,
                "tool_batch_size_invalid",
                $"Tool batches must contain between 1 and {MaximumBatchSize} calls.",
                false);
        }

        var continueOnError = payload["continueOnError"]?.GetValue<bool>() ?? false;
        var results = new JsonArray();
        var completed = 0;
        for (var index = 0; index < calls.Count; index++)
        {
            if (calls[index] is not JsonObject call)
            {
                results.Add(CreateBatchInputError(index));
                if (!continueOnError)
                {
                    break;
                }

                continue;
            }

            var outcome = await ExecuteCallAsync(request, call, cancellationToken);
            var item = outcome.ToJson();
            item["index"] = index;
            item["callId"] = call["callId"]?.DeepClone();
            results.Add(item);
            completed++;
            if (!outcome.IsSuccess && !continueOnError)
            {
                break;
            }
        }

        var batchResult = new JsonObject
        {
            ["success"] = results.All(result => result?["success"]?.GetValue<bool>() == true),
            ["completed"] = completed,
            ["requested"] = calls.Count,
            ["stoppedEarly"] = results.Count < calls.Count,
            ["results"] = results
        };
        return CreateResultEnvelope(request, BatchResultType, batchResult);
    }

    private async Task<ToolCallExecutionOutcome> ExecuteCallAsync(
        ToolProtocolEnvelope request,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var toolId = payload["toolId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolId))
        {
            return ToolCallExecutionOutcome.Failure(
                null,
                "tool_call_missing_id",
                "Tool call payload must include 'toolId'.");
        }

        if (!registry.TryGet(toolId, out var tool) || tool == null)
        {
            return ToolCallExecutionOutcome.Failure(
                toolId,
                "tool_not_found",
                $"Tool '{toolId}' is not registered.");
        }

        if (!guard.CanExecute(tool, out var denialReason))
        {
            return ToolCallExecutionOutcome.Failure(
                toolId,
                "tool_execution_denied",
                denialReason ?? "Tool execution is denied by the current guard policy.");
        }

        var availability = await tool.GetAvailabilityAsync(
            new ToolAvailabilityContext(request.SessionId, request.Id));
        if (!availability.IsAvailable)
        {
            return ToolCallExecutionOutcome.Failure(
                toolId,
                availability.ReasonCode ?? "tool_unavailable",
                availability.Reason ?? $"Tool '{toolId}' is not available in the current runtime state.",
                availability.Retryable,
                availability.ToJson());
        }

        var arguments = payload["arguments"] switch
        {
            null => new JsonObject(),
            JsonObject jsonArguments => (JsonObject)jsonArguments.DeepClone(),
            _ => null
        };
        if (arguments is null)
        {
            return ToolCallExecutionOutcome.Failure(
                toolId,
                "tool_arguments_invalid",
                "Tool arguments must be a JSON object.");
        }

        try
        {
            ToolResult result;
            if (tool is IJsonTool jsonTool)
            {
                var argumentValidation = ToolSchemaValidator.Validate(tool.ArgumentsSchema, arguments);
                if (!argumentValidation.IsValid)
                {
                    return ToolCallExecutionOutcome.Failure(
                        toolId,
                        "tool_arguments_schema_invalid",
                        $"Arguments for '{toolId}' do not satisfy its schema.",
                        payload: argumentValidation.ToJson());
                }

                result = await jsonTool.ExecuteAsync(
                    new ToolInvocation(
                        arguments,
                        new ToolInvocationContext(
                            request.Id,
                            request.SessionId,
                            payload["callId"]?.GetValue<string>())),
                    cancellationToken);
            }
            else
            {
                result = await tool.Execute(CreateLegacyArguments(arguments, request));
            }

            if (!result.IsSuccess)
            {
                return ToolCallExecutionOutcome.Failure(
                    toolId,
                    result.ErrorCode ?? "tool_execution_failed",
                    result.Message ?? $"Tool '{toolId}' failed.",
                    payload: result.Payload);
            }

            if (tool is IJsonTool)
            {
                var resultValidation = ToolSchemaValidator.Validate(tool.ResultSchema, result.Payload);
                if (!resultValidation.IsValid)
                {
                    return ToolCallExecutionOutcome.Failure(
                        toolId,
                        "tool_result_schema_invalid",
                        $"Result from '{toolId}' does not satisfy its schema.",
                        payload: resultValidation.ToJson());
                }
            }

            var evidence = await CaptureEvidenceAsync(request, payload, toolId, cancellationToken);
            return ToolCallExecutionOutcome.Success(toolId, result.Message, result.Payload, evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ToolCallExecutionOutcome.Failure(
                toolId,
                "tool_execution_exception",
                exception.Message);
        }
    }

    private async Task<JsonObject?> CaptureEvidenceAsync(
        ToolProtocolEnvelope request,
        JsonObject call,
        string invokedToolId,
        CancellationToken cancellationToken)
    {
        if (call["after"] is not JsonObject after)
        {
            return null;
        }

        var delayMilliseconds = Math.Clamp(
            after["delayMilliseconds"]?.GetValue<int>() ?? 0,
            0,
            MaximumEvidenceDelayMilliseconds);
        if (delayMilliseconds > 0)
        {
            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        var include = after["include"] as JsonArray ?? new JsonArray("visualTree");
        var evidence = new JsonObject();
        foreach (var requestedEvidence in include)
        {
            var evidenceName = requestedEvidence?.GetValue<string>();
            var evidenceToolId = evidenceName switch
            {
                "tree" or "visualTree" or "visual_tree" => "ui.get_visual_tree",
                "screenshot" => "ui.get_screenshot",
                _ => null
            };
            if (evidenceToolId is null || string.Equals(evidenceToolId, invokedToolId, StringComparison.Ordinal))
            {
                continue;
            }

            var argumentName = evidenceToolId == "ui.get_screenshot"
                ? "screenshotArguments"
                : "visualTreeArguments";
            var evidenceCall = new JsonObject
            {
                ["toolId"] = evidenceToolId,
                ["arguments"] = after[argumentName]?.DeepClone() ?? new JsonObject()
            };
            evidence[evidenceName ?? evidenceToolId] =
                (await ExecuteCallAsync(request, evidenceCall, cancellationToken)).ToJson();
        }

        return evidence.Count == 0 ? null : evidence;
    }

    private static Dictionary<string, string> CreateLegacyArguments(
        JsonObject arguments,
        ToolProtocolEnvelope request)
    {
        var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in arguments)
        {
            if (property.Value == null)
            {
                continue;
            }

            flattened[property.Key] = property.Value is JsonValue value
                ? value.ToString()
                : property.Value.ToJsonString();
        }

        flattened[ToolExecutionArgumentNames.RequestId] = request.Id;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            flattened[ToolExecutionArgumentNames.SessionId] = request.SessionId;
        }

        return flattened;
    }

    private ToolProtocolEnvelope CreateResultEnvelope(
        ToolProtocolEnvelope request,
        string type,
        JsonObject payload)
        => new()
        {
            Type = type,
            Id = CreateResponseId(request.Id),
            ReplyTo = request.Id,
            SessionId = request.SessionId,
            Capability = Capability,
            SentAt = DateTimeOffset.UtcNow,
            Payload = ToolProtocolPayloadEncoding.EncodeIfBeneficial(payload, Pairing.PairingJson.Compact)
        };

    private ToolProtocolEnvelope CreateErrorEnvelope(
        ToolProtocolEnvelope request,
        string code,
        string message,
        bool retryable,
        JsonNode? details = null)
        => new()
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

    private string ComputeCatalogRevision(IReadOnlyList<ITool> tools)
    {
        var revisionInput = new JsonObject
        {
            ["schema"] = CatalogSchema,
            ["guard"] = guard.ToJson(),
            ["tools"] = new JsonArray(
                tools
                    .OrderBy(tool => tool.Id, StringComparer.Ordinal)
                    .Select(tool => (JsonNode?)ToStaticJson(tool))
                    .ToArray())
        };
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(revisionInput.ToJsonString()));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static ToolCapabilityManifest CreateCapabilityManifest(
        IReadOnlyList<ITool> tools,
        string revision)
    {
        var capabilities = new Dictionary<string, ToolCapabilityDefinition>(StringComparer.Ordinal)
        {
            [Capability] = new(
                2,
                [
                    "batch",
                    "catalog-revision",
                    "json-arguments",
                    "post-evidence",
                    "runtime-availability",
                    "schema-validation"
                ])
        };
        foreach (var category in tools.GroupBy(tool => tool.Category, StringComparer.Ordinal))
        {
            capabilities[$"{category}.tools"] = new(
                1,
                category.Select(tool => tool.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }

        return new ToolCapabilityManifest(
            ToolCapabilityManifest.CurrentSchema,
            revision,
            capabilities);
    }

    private static JsonObject ToJson(ITool tool, ToolAvailability availability)
    {
        var json = ToStaticJson(tool);
        json["runtime"] = availability.ToJson();
        json["executable"] = availability.IsAvailable;
        return json;
    }

    private static JsonObject ToStaticJson(ITool tool)
    {
        var definition = tool.Definition;
        return new JsonObject
        {
            ["id"] = definition.Id,
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["category"] = definition.Category,
            ["policy"] = definition.Policy.ToString().ToLowerInvariant(),
            ["keywords"] = definition.Keywords,
            ["argumentEncoding"] = tool is IJsonTool ? "json" : "flattened-string",
            ["argumentsSchema"] = definition.ArgumentsSchema.ToJson(),
            ["resultSchema"] = definition.ResultSchema.ToJson()
        };
    }

    private static JsonObject CreateBatchInputError(int index)
        => new()
        {
            ["index"] = index,
            ["success"] = false,
            ["error"] = new JsonObject
            {
                ["code"] = "tool_batch_call_invalid",
                ["message"] = "Each batch call must be a JSON object.",
                ["retryable"] = false
            }
        };

    private static string CreateResponseId(string requestId) => $"{requestId}.response";

    private sealed record ToolCallExecutionOutcome(
        bool IsSuccess,
        string? ToolId,
        string? Message,
        string? ErrorCode,
        bool Retryable,
        JsonNode? Payload,
        JsonObject? Evidence)
    {
        public static ToolCallExecutionOutcome Success(
            string toolId,
            string? message,
            JsonNode? payload,
            JsonObject? evidence)
            => new(true, toolId, message, null, false, payload, evidence);

        public static ToolCallExecutionOutcome Failure(
            string? toolId,
            string errorCode,
            string message,
            bool retryable = false,
            JsonNode? payload = null)
            => new(false, toolId, message, errorCode, retryable, payload, null);

        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["toolId"] = ToolId,
                ["success"] = IsSuccess,
                ["message"] = Message
            };
            if (IsSuccess)
            {
                json["result"] = Payload;
                json["evidence"] = Evidence;
            }
            else
            {
                json["error"] = new JsonObject
                {
                    ["code"] = ErrorCode,
                    ["message"] = Message,
                    ["retryable"] = Retryable,
                    ["details"] = Payload
                };
            }

            return json;
        }
    }
}
