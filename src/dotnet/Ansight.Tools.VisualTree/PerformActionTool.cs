namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

/// <summary>
/// Performs a framework-neutral action against a snapshot-scoped node reference.
/// </summary>
public sealed class PerformActionTool : IJsonTool
{
    public string Category => "ui";
    public ToolScope Scope => ToolScope.Write;
    public string Id => VisualTreeToolIds.PerformAction;
    public string Name => "Perform UI Action";
    public string Description => "Performs a generic action against a current snapshot-scoped UI node.";
    public string Keywords => "ui action tap focus set value toggle select node snapshot";
    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.PerformActionArguments;
    public ToolSchema ResultSchema => VisualTreeToolSchemas.PerformActionResult;
    public ToolSecurity Security => VisualTreeToolSecurityProfiles.PerformAction;

    public async Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        var reference = invocation.Arguments["reference"] as JsonObject;
        var source = invocation.Arguments["source"]?.GetValue<string>()
                     ?? reference?["source"]?.GetValue<string>();
        var snapshotId = invocation.Arguments["snapshotId"]?.GetValue<string>()
                         ?? reference?["snapshotId"]?.GetValue<string>();
        var nodeId = invocation.Arguments["nodeId"]?.GetValue<string>()
                     ?? reference?["nodeId"]?.GetValue<string>();
        var action = invocation.Arguments["action"]!.GetValue<string>();
        if (string.IsNullOrWhiteSpace(snapshotId) || string.IsNullOrWhiteSpace(nodeId))
        {
            return ToolResult.Failure(
                "A reference, or both snapshotId and nodeId, is required.",
                errorCode: "ui_action_reference_required");
        }
        if (!VisualTreeSnapshotStore.TryValidateNodeReference(
                snapshotId,
                source,
                nodeId,
                out var snapshot,
                out var referenceError))
        {
            return referenceError!;
        }

        if (!VisualTreeProviderRegistry.TryGet(snapshot!.Source, out var provider)
            || provider is not IVisualTreeInteractionProvider interactionProvider)
        {
            return ToolResult.Failure(
                $"Visual-tree source '{snapshot.Source}' does not support generic UI actions.",
                errorCode: "ui_action_not_supported");
        }

        var value = invocation.Arguments["value"]?.DeepClone()
                    ?? invocation.Arguments["index"]?.DeepClone()
                    ?? invocation.Arguments["checked"]?.DeepClone();
        var options = invocation.Arguments["options"] as JsonObject ?? new JsonObject();
        var result = await interactionProvider.PerformActionAsync(
            new VisualTreeActionRequest(nodeId, action, value, (JsonObject)options.DeepClone()),
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.ErrorCode is "visual_tree_node_not_found" or "maui_node_not_found")
            {
                return ToolResult.Failure(
                    $"Node '{nodeId}' is no longer valid for snapshot '{snapshotId}'. Refresh the query and retry.",
                    errorCode: "stale_node_reference",
                    payload: new JsonObject
                    {
                        ["reference"] = VisualTreeSnapshotStore.CreateReference(snapshot, nodeId),
                        ["providerError"] = result.ErrorCode,
                        ["refreshWith"] = VisualTreeToolIds.QueryNodes
                    });
            }

            return result;
        }

        var payload = result.Payload as JsonObject ?? new JsonObject { ["providerResult"] = result.Payload };
        payload["source"] = snapshot.Source;
        payload["action"] = action;
        payload["reference"] = VisualTreeSnapshotStore.CreateReference(snapshot, nodeId);
        return ToolResult.Success(payload, result.Message);
    }
}
