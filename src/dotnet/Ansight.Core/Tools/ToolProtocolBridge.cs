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
    public const string CatalogSchema = "ansight.tool-catalog.v3";

    private const int MaximumBatchSize = 32;
    private const int MaximumEvidenceDelayMilliseconds = 2_000;
    private const int MaximumCatalogResults = 1_000;
    private const string FullCatalogDetail = "full";
    private const string IndexCatalogDetail = "index";
    private const string DefinitionsCatalogDetail = "definitions";
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

        var requestPayload = request.Payload as JsonObject;
        var detail = ReadCatalogDetail(requestPayload);
        var visibleTools = registry
            .Where(guard.IsToolVisible)
            .OrderBy(tool => tool.Id, StringComparer.Ordinal)
            .ToList();
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var toolStates = new List<CatalogToolState>(visibleTools.Count);
        foreach (var tool in visibleTools)
        {
            var availability = await tool.GetAvailabilityAsync(
                new ToolAvailabilityContext(request.SessionId, request.Id));
            toolStates.Add(CreateCatalogToolState(tool, availability));
        }

        var revision = ComputeCatalogRevision(toolStates);
        var availabilityRevision = ComputeAvailabilityRevision(toolStates);
        var requestedRevision = ReadString(requestPayload?["ifRevision"]);
        var requestedAvailabilityRevision = ReadString(requestPayload?["ifAvailabilityRevision"]);
        var staticUnchanged = string.Equals(requestedRevision, revision, StringComparison.Ordinal);
        var availabilityUnchanged = string.IsNullOrWhiteSpace(requestedAvailabilityRevision)
            || string.Equals(requestedAvailabilityRevision, availabilityRevision, StringComparison.Ordinal);
        var isDefinitionProjection = string.Equals(detail, DefinitionsCatalogDetail, StringComparison.Ordinal);

        if (staticUnchanged && availabilityUnchanged && !isDefinitionProjection)
        {
            return CreateResponseEnvelope(
                request,
                CatalogType,
                new JsonObject
                {
                    ["schema"] = CatalogSchema,
                    ["revision"] = revision,
                    ["unchanged"] = true
                });
        }

        if (staticUnchanged && !availabilityUnchanged && !isDefinitionProjection)
        {
            return CreateResponseEnvelope(
                request,
                CatalogType,
                new JsonObject
                {
                    ["schema"] = CatalogSchema,
                    ["revision"] = revision,
                    ["unchanged"] = true,
                    ["availabilityRevision"] = availabilityRevision,
                    ["evaluatedAtUtc"] = evaluatedAtUtc,
                    ["changes"] = CreateAvailabilityChanges(toolStates)
                });
        }

        var selectedStates = ApplyCatalogFilters(toolStates, requestPayload);
        var serializedTools = new JsonArray();
        foreach (var state in selectedStates)
        {
            serializedTools.Add(detail switch
            {
                IndexCatalogDetail => ToIndexJson(state),
                DefinitionsCatalogDetail => ToDefinitionJson(state),
                _ => ToFullJson(state)
            });
        }

        var catalogPayload = new JsonObject
        {
            ["schema"] = CatalogSchema,
            ["revision"] = revision,
            ["tools"] = serializedTools,
            ["count"] = serializedTools.Count
        };
        if (!string.Equals(detail, FullCatalogDetail, StringComparison.Ordinal))
        {
            catalogPayload["detail"] = detail;
        }

        if (!isDefinitionProjection)
        {
            catalogPayload["availabilityRevision"] = availabilityRevision;
            catalogPayload["evaluatedAtUtc"] = evaluatedAtUtc;
            catalogPayload["totalCount"] = toolStates.Count;
            catalogPayload["categories"] = CreateCategoryCounts(toolStates);
        }

        return CreateResponseEnvelope(request, CatalogType, catalogPayload);
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
            ? CreateResponseEnvelope(request, ResultType, outcome.ToJson())
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
        return CreateResponseEnvelope(request, BatchResultType, batchResult);
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

    private ToolProtocolEnvelope CreateResponseEnvelope(
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
        => CreateResponseEnvelope(
            request,
            ErrorType,
            new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["retryable"] = retryable,
                ["details"] = details
            });

    private string ComputeCatalogRevision(IReadOnlyList<CatalogToolState> states)
        => ComputeRevision(new JsonObject
        {
            ["schema"] = CatalogSchema,
            ["guard"] = guard.ToJson(),
            ["tools"] = new JsonArray(states
                .OrderBy(state => state.Tool.Id, StringComparer.Ordinal)
                .Select(state => (JsonNode?)new JsonObject
                {
                    ["id"] = state.Tool.Id,
                    ["definitionRevision"] = state.DefinitionRevision
                })
                .ToArray())
        });

    private static string ComputeAvailabilityRevision(IReadOnlyList<CatalogToolState> states)
        => ComputeRevision(new JsonObject
        {
            ["tools"] = new JsonArray(states
                .OrderBy(state => state.Tool.Id, StringComparer.Ordinal)
                .Select(state => (JsonNode?)new JsonObject
                {
                    ["id"] = state.Tool.Id,
                    ["runtime"] = CreateAvailabilityRevisionJson(state.Availability)
                })
                .ToArray())
        });

    private static CatalogToolState CreateCatalogToolState(ITool tool, ToolAvailability availability)
    {
        var staticDefinition = CreateStaticDefinitionJson(tool);
        return new CatalogToolState(
            tool,
            availability,
            ComputeRevision(staticDefinition));
    }

    private static JsonObject ToIndexJson(CatalogToolState state)
    {
        var definition = state.Tool.Definition;
        var json = new JsonObject
        {
            ["id"] = definition.Id,
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["category"] = definition.Category,
            ["policy"] = definition.Policy.ToString().ToLowerInvariant(),
            ["definitionRevision"] = state.DefinitionRevision
        };
        AddOptionalDiscoveryMetadata(json, definition);
        AddAvailability(json, state.Availability);
        return json;
    }

    private static JsonObject ToDefinitionJson(CatalogToolState state)
    {
        var json = CreateStaticDefinitionJson(state.Tool);
        json["definitionRevision"] = state.DefinitionRevision;
        return json;
    }

    private static JsonObject ToFullJson(CatalogToolState state)
    {
        var json = ToDefinitionJson(state);
        AddAvailability(json, state.Availability);
        return json;
    }

    private static JsonObject CreateStaticDefinitionJson(ITool tool)
    {
        var definition = tool.Definition;
        var json = new JsonObject
        {
            ["id"] = definition.Id,
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["category"] = definition.Category,
            ["policy"] = definition.Policy.ToString().ToLowerInvariant(),
            ["argumentsSchema"] = ToProtocolSchemaJson(definition.ArgumentsSchema),
            ["resultSchema"] = ToProtocolSchemaJson(definition.ResultSchema)
        };
        AddOptionalDiscoveryMetadata(json, definition);
        if (tool is not IJsonTool)
        {
            json["argumentEncoding"] = "flattened-string";
        }

        return json;
    }

    private static void AddOptionalDiscoveryMetadata(JsonObject json, ToolDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Keywords))
        {
            json["keywords"] = definition.Keywords;
        }

        var prerequisiteToolIds = (definition.PrerequisiteToolIds ?? Array.Empty<string>())
            .Where(toolId => !string.IsNullOrWhiteSpace(toolId))
            .Select(toolId => toolId.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(toolId => toolId, StringComparer.Ordinal)
            .ToArray();
        if (prerequisiteToolIds.Length > 0)
        {
            json["prerequisiteToolIds"] = new JsonArray(
                prerequisiteToolIds.Select(toolId => (JsonNode?)toolId).ToArray());
        }
    }

    private static void AddAvailability(JsonObject json, ToolAvailability availability)
    {
        if (availability.IsAvailable)
        {
            return;
        }

        json["runtime"] = CreateCompactAvailabilityJson(availability);
        json["executable"] = false;
    }

    private static JsonObject CreateCompactAvailabilityJson(ToolAvailability availability)
    {
        var json = new JsonObject
        {
            ["available"] = availability.IsAvailable
        };
        if (!string.IsNullOrWhiteSpace(availability.ReasonCode))
        {
            json["code"] = availability.ReasonCode;
        }

        if (!string.IsNullOrWhiteSpace(availability.Reason))
        {
            json["reason"] = availability.Reason;
        }

        if (!string.IsNullOrWhiteSpace(availability.RequiredState))
        {
            json["requiredState"] = availability.RequiredState;
        }

        if (!string.IsNullOrWhiteSpace(availability.Remediation))
        {
            json["remediation"] = availability.Remediation;
        }

        if (availability.Retryable)
        {
            json["retryable"] = true;
        }

        return json;
    }

    private static JsonObject CreateAvailabilityRevisionJson(ToolAvailability availability)
        => availability.IsAvailable
            ? new JsonObject { ["available"] = true }
            : CreateCompactAvailabilityJson(availability);

    private static JsonObject CreateAvailabilityChanges(IReadOnlyList<CatalogToolState> states)
    {
        var changes = new JsonObject();
        foreach (var state in states.Where(state => !state.Availability.IsAvailable))
        {
            changes[state.Tool.Id] = CreateCompactAvailabilityJson(state.Availability);
        }

        return changes;
    }

    private static JsonObject CreateCategoryCounts(IReadOnlyList<CatalogToolState> states)
    {
        var categories = new JsonObject();
        foreach (var category in states
                     .GroupBy(state => state.Tool.Category, StringComparer.Ordinal)
                     .OrderBy(category => category.Key, StringComparer.Ordinal))
        {
            categories[category.Key] = category.Count();
        }

        return categories;
    }

    private static IReadOnlyList<CatalogToolState> ApplyCatalogFilters(
        IReadOnlyList<CatalogToolState> states,
        JsonObject? payload)
    {
        var requestedIds = ReadStringArray(payload?["ids"]);
        var requestedIdSet = requestedIds.Count == 0
            ? null
            : requestedIds.ToHashSet(StringComparer.Ordinal);
        var queryTerms = SplitTerms(ReadString(payload?["query"]));
        var featureTerms = SplitTerms(ReadString(payload?["feature"]));
        var policy = ReadString(payload?["policy"]);
        var executableOnly = ReadBoolean(payload?["executableOnly"], fallback: false);
        var limit = Math.Clamp(
            ReadInteger(payload?["limit"] ?? payload?["maxResults"], MaximumCatalogResults),
            1,
            MaximumCatalogResults);

        return states
            .Where(state => requestedIdSet is null || requestedIdSet.Contains(state.Tool.Id))
            .Where(state => policy is null
                            || string.Equals(
                                state.Tool.Policy.ToString(),
                                policy,
                                StringComparison.OrdinalIgnoreCase))
            .Where(state => !executableOnly || state.Availability.IsAvailable)
            .Where(state => MatchesTerms(state.Tool.Definition, queryTerms, featureTerms))
            .Take(limit)
            .ToArray();
    }

    private static bool MatchesTerms(
        ToolDefinition definition,
        IReadOnlyList<string> queryTerms,
        IReadOnlyList<string> featureTerms)
    {
        var searchableText = NormalizeSearchText(string.Join(
            ' ',
            definition.Id,
            definition.Name,
            definition.Description,
            definition.Category,
            definition.Keywords,
            string.Join(' ', definition.PrerequisiteToolIds ?? Array.Empty<string>())));
        return queryTerms.All(searchableText.Contains)
               && featureTerms.All(searchableText.Contains);
    }

    private static string ReadCatalogDetail(JsonObject? payload)
    {
        var detail = ReadString(payload?["detail"])?.ToLowerInvariant();
        return detail is IndexCatalogDetail or DefinitionsCatalogDetail or FullCatalogDetail
            ? detail
            : FullCatalogDetail;
    }

    private static string? ReadString(JsonNode? value)
        => value is JsonValue jsonValue
           && jsonValue.TryGetValue<string>(out var text)
           && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonNode? value)
        => value is JsonArray array
            ? array
                .Select(ReadString)
                .Where(text => text is not null)
                .Select(text => text!)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

    private static bool ReadBoolean(JsonNode? value, bool fallback)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var result)
            ? result
            : fallback;

    private static int ReadInteger(JsonNode? value, int fallback)
        => value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var result)
            ? result
            : fallback;

    private static IReadOnlyList<string> SplitTerms(string? value)
        => NormalizeSearchText(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
    }

    private static JsonObject ToProtocolSchemaJson(ToolSchema schema)
    {
        var json = new JsonObject
        {
            ["type"] = schema.Nullable
                ? new JsonArray(ToJsonType(schema.Type), "null")
                : ToJsonType(schema.Type)
        };
        if (schema.AdditionalProperties)
        {
            json["additionalProperties"] = true;
        }

        if (!string.IsNullOrWhiteSpace(schema.Description))
        {
            json["description"] = schema.Description;
        }

        if (!string.IsNullOrWhiteSpace(schema.Format))
        {
            json["format"] = schema.Format;
        }

        if (schema.EnumValues.Count > 0)
        {
            json["enum"] = new JsonArray(schema.EnumValues.Select(value => (JsonNode?)value).ToArray());
        }

        if (schema.Items is not null)
        {
            json["items"] = ToProtocolSchemaJson(schema.Items);
        }

        if (schema.Properties.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var property in schema.Properties.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                properties[property.Key] = ToProtocolSchemaJson(property.Value);
            }

            json["properties"] = properties;
        }

        if (schema.Required.Count > 0)
        {
            json["required"] = new JsonArray(schema.Required.Select(value => (JsonNode?)value).ToArray());
        }

        return json;
    }

    private static string ToJsonType(ToolSchemaType type) => type switch
    {
        ToolSchemaType.Object => "object",
        ToolSchemaType.Array => "array",
        ToolSchemaType.String => "string",
        ToolSchemaType.Integer => "integer",
        ToolSchemaType.Number => "number",
        ToolSchemaType.Boolean => "boolean",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static string ComputeRevision(JsonNode value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonString()));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
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

    private sealed record CatalogToolState(
        ITool Tool,
        ToolAvailability Availability,
        string DefinitionRevision);

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
