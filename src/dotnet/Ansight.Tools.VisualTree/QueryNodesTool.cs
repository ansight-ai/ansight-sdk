namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

/// <summary>
/// Searches any registered visual-tree provider through one framework-neutral contract.
/// </summary>
public sealed class QueryNodesTool : IJsonTool
{
    public string Category => "ui";
    public ToolScope Scope => ToolScope.Read;
    public string Id => VisualTreeToolIds.QueryNodes;
    public string Name => "Query UI Nodes";
    public string Description => "Captures or reuses a UI snapshot and returns framework-neutral node references.";
    public string Keywords => "ui query find node selector automation id role text type";
    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.QueryNodesArguments;
    public ToolSchema ResultSchema => VisualTreeToolSchemas.QueryNodesResult;
    public ToolSecurity Security => VisualTreeToolSecurityProfiles.QueryNodes;

    public async Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var source = invocation.Arguments["source"]?.GetValue<string>();
        var snapshotId = invocation.Arguments["snapshotId"]?.GetValue<string>();
        VisualTreeSnapshot snapshot;
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            var capture = await VisualTreeSnapshotStore.CaptureAsync(
                source,
                VisualTreeToolArguments.Flatten(invocation),
                cancellationToken);
            if (!capture.IsSuccess || capture.Payload is not JsonObject capturePayload)
            {
                return capture;
            }

            snapshotId = capturePayload["snapshotId"]!.GetValue<string>();
            if (!VisualTreeSnapshotStore.TryGet(snapshotId, source, true, out var capturedSnapshot, out var error))
            {
                return error!;
            }

            snapshot = capturedSnapshot!;
        }
        else if (!VisualTreeSnapshotStore.TryGet(snapshotId, source, true, out var storedSnapshot, out var error))
        {
            return error!;
        }
        else
        {
            snapshot = storedSnapshot!;
        }

        var maxResults = Math.Clamp(invocation.Arguments["maxResults"]?.GetValue<int>() ?? 50, 1, 500);
        var matches = new JsonArray();
        var totalMatches = 0;
        if (snapshot.Payload["root"] is JsonObject root)
        {
            foreach (var node in EnumerateNodes(root))
            {
                if (!Matches(snapshot.Payload, node, invocation.Arguments))
                {
                    continue;
                }

                totalMatches++;
                if (matches.Count < maxResults)
                {
                    var match = (JsonObject)node.DeepClone();
                    match["reference"] = VisualTreeSnapshotStore.CreateReference(
                        snapshot,
                        node["id"]!.GetValue<string>());
                    match["type"] = ResolveType(snapshot.Payload, node);
                    match["visible"] = ReadState(snapshot.Payload, node, "visible", 1);
                    match["enabled"] = ReadState(snapshot.Payload, node, "enabled", 2);
                    match["supportedActions"] ??= InferActions(match["type"]?.GetValue<string>());
                    matches.Add(match);
                }
            }
        }

        return ToolResult.Success(new JsonObject
        {
            ["source"] = snapshot.Source,
            ["snapshotId"] = snapshot.SnapshotId,
            ["revision"] = snapshot.Revision,
            ["count"] = matches.Count,
            ["totalMatches"] = totalMatches,
            ["truncated"] = totalMatches > matches.Count,
            ["matches"] = matches
        });
    }

    private static IEnumerable<JsonObject> EnumerateNodes(JsonObject node)
    {
        yield return node;
        if (node["children"] is not JsonArray children)
        {
            yield break;
        }

        foreach (var child in children.OfType<JsonObject>())
        {
            foreach (var descendant in EnumerateNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool Matches(JsonObject payload, JsonObject node, JsonObject arguments)
    {
        if (node["id"]?.GetValue<string>() is not { Length: > 0 } nodeId)
        {
            return false;
        }

        if (!EqualsFilter(nodeId, arguments, "nodeId")
            || !EqualsFilter(node["automationId"]?.GetValue<string>(), arguments, "automationId")
            || !EqualsFilter(node["role"]?.GetValue<string>(), arguments, "role")
            || !ContainsFilter(ResolveType(payload, node), arguments, "type")
            || !ContainsFilter(ResolveSearchText(node), arguments, "textContains"))
        {
            return false;
        }

        if (arguments["visible"] is JsonValue visible
            && ReadState(payload, node, "visible", 1) != visible.GetValue<bool>())
        {
            return false;
        }

        if (arguments["enabled"] is JsonValue enabled
            && ReadState(payload, node, "enabled", 2) != enabled.GetValue<bool>())
        {
            return false;
        }

        if (arguments["action"]?.GetValue<string>() is { Length: > 0 } action)
        {
            var actions = node["supportedActions"] as JsonArray
                          ?? InferActions(ResolveType(payload, node));
            if (!actions.Any(candidate => string.Equals(
                    candidate?.GetValue<string>(),
                    action,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EqualsFilter(string? value, JsonObject arguments, string name)
        => arguments[name]?.GetValue<string>() is not { Length: > 0 } filter
           || string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsFilter(string? value, JsonObject arguments, string name)
        => arguments[name]?.GetValue<string>() is not { Length: > 0 } filter
           || value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    private static string? ResolveType(JsonObject payload, JsonObject node)
    {
        if (node["type"]?.GetValue<string>() is { Length: > 0 } type)
        {
            return type;
        }

        if (node["typeId"] is JsonValue typeIdValue
            && typeIdValue.TryGetValue<int>(out var typeId)
            && payload["types"] is JsonArray types
            && typeId >= 0
            && typeId < types.Count)
        {
            return types[typeId]?.GetValue<string>();
        }

        return null;
    }

    private static string ResolveSearchText(JsonObject node)
        => string.Join(
            " ",
            new[]
            {
                node["label"]?.GetValue<string>(),
                node["title"]?.GetValue<string>(),
                node["visual"]?["text"]?.GetValue<string>(),
                node["visual"]?["value"]?.GetValue<string>()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static bool ReadState(JsonObject payload, JsonObject node, string propertyName, int fallbackBit)
    {
        if (node[propertyName] is JsonValue explicitValue
            && explicitValue.TryGetValue<bool>(out var state))
        {
            return state;
        }

        var bit = payload["flagBits"]?[propertyName]?.GetValue<int>() ?? fallbackBit;
        return node["flags"] is JsonValue flagsValue
               && flagsValue.TryGetValue<int>(out var flags)
               && (flags & bit) == bit;
    }

    private static JsonArray InferActions(string? type)
    {
        var normalized = type?.ToLowerInvariant() ?? string.Empty;
        var actions = new JsonArray();
        if (normalized.Contains("button") || normalized.Contains("tap"))
        {
            actions.Add("tap");
        }

        if (normalized.Contains("entry") || normalized.Contains("editor") || normalized.Contains("textfield"))
        {
            actions.Add("focus");
            actions.Add("setValue");
        }

        if (normalized.Contains("checkbox") || normalized.Contains("switch"))
        {
            actions.Add("toggle");
        }

        if (normalized.Contains("picker"))
        {
            actions.Add("select");
        }

        return actions;
    }
}
