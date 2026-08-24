namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

public sealed class InspectNodeTool : ITool
{
    public string Category => "ui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => VisualTreeToolIds.InspectNode;

    public string Name => "Inspect Node";

    public string Description => "Returns detailed metadata for a visual tree node.";

    public string Keywords => "ui node inspect accessibility layout";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.InspectNodeArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.InspectNodeResult;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var reference = ParseReference(arguments);
        var source = GetArgument(arguments, "source") ?? reference?["source"]?.GetValue<string>();
        var nodeId = GetArgument(arguments, "nodeId")
            ?? GetArgument(arguments, "id")
            ?? reference?["nodeId"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return ToolResult.Failure("Node id is required.", errorCode: "node_id_required");
        }

        var providerArguments = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId
        };
        if (!string.IsNullOrWhiteSpace(source))
        {
            providerArguments["source"] = source;
        }

        if (!VisualTreeProviderRegistry.TryGet(source, out var provider) || provider is null)
        {
            var normalizedSource = VisualTreeProviderRegistry.NormalizeSourceOrDefault(source);
            return ToolResult.Failure(
                $"No visual tree provider is registered for source '{normalizedSource}'.",
                errorCode: "visual_tree_provider_not_found");
        }

        VisualTreeSnapshot snapshot;
        var snapshotId = GetArgument(arguments, "snapshotId")
            ?? reference?["snapshotId"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(snapshotId))
        {
            if (!VisualTreeSnapshotStore.TryValidateNodeReference(
                    snapshotId,
                    source,
                    nodeId!,
                    out var storedSnapshot,
                    out var referenceError))
            {
                return referenceError!;
            }

            snapshot = storedSnapshot!;
        }
        else
        {
            var capture = await VisualTreeSnapshotStore.CaptureAsync(source, providerArguments);
            if (!capture.IsSuccess || capture.Payload is not JsonObject capturePayload)
            {
                return capture;
            }

            snapshotId = capturePayload["snapshotId"]!.GetValue<string>();
            if (!VisualTreeSnapshotStore.TryValidateNodeReference(
                    snapshotId,
                    source,
                    nodeId!,
                    out var capturedSnapshot,
                    out var referenceError))
            {
                return referenceError!;
            }

            snapshot = capturedSnapshot!;
        }

        providerArguments["source"] = snapshot.Source;
        providerArguments["snapshotId"] = snapshot.SnapshotId;
        var result = await provider.InspectNodeAsync(providerArguments);
        if (!result.IsSuccess)
        {
            if (result.ErrorCode is "visual_tree_node_not_found" or "maui_node_not_found")
            {
                return ToolResult.Failure(
                    $"Node '{nodeId}' is no longer valid for snapshot '{snapshot.SnapshotId}'. Refresh the UI query and retry.",
                    errorCode: "stale_node_reference",
                    payload: new JsonObject
                    {
                        ["reference"] = VisualTreeSnapshotStore.CreateReference(snapshot, nodeId!),
                        ["providerError"] = result.ErrorCode,
                        ["refreshWith"] = VisualTreeToolIds.QueryNodes
                    });
            }

            return result;
        }

        var payload = result.Payload as JsonObject ?? new JsonObject();
        payload["source"] = snapshot.Source;
        payload["snapshotId"] = snapshot.SnapshotId;
        payload["revision"] = snapshot.Revision;
        payload["reference"] = VisualTreeSnapshotStore.CreateReference(snapshot, nodeId!);
        return ToolResult.Success(payload, result.Message);
    }

    private static string? GetArgument(IReadOnlyDictionary<string, string> arguments, string name)
        => arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static JsonObject? ParseReference(IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("reference", out var json) || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
