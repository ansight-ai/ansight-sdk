namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

internal static class VisualTreeSnapshotStore
{
    private const int MaximumSnapshots = 32;
    private static readonly Lock gate = new();
    private static readonly Dictionary<string, VisualTreeSnapshot> snapshots = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> latestRevisions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> insertionOrder = new();
    private static long nextRevision;

    internal static async Task<ToolResult> CaptureAsync(
        string? source,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = VisualTreeProviderRegistry.NormalizeSourceOrDefault(source);
        if (!VisualTreeProviderRegistry.TryGet(normalizedSource, out var provider) || provider is null)
        {
            return ToolResult.Failure(
                $"No visual tree provider is registered for source '{normalizedSource}'.",
                errorCode: "visual_tree_provider_not_found");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await provider.GetVisualTreeAsync(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.IsSuccess || result.Payload is not JsonObject payload)
        {
            return result;
        }

        var revision = Interlocked.Increment(ref nextRevision);
        var snapshotId = $"{normalizedSource}:{revision}:{Guid.NewGuid():N}";
        payload["source"] = normalizedSource;
        payload["snapshotId"] = snapshotId;
        payload["revision"] = revision;
        payload["nodeIdentity"] = new JsonObject
        {
            ["scope"] = "snapshot",
            ["source"] = normalizedSource,
            ["staleAfterRevision"] = revision
        };

        var storedPayload = (JsonObject)payload.DeepClone();
        var snapshot = new VisualTreeSnapshot(
            snapshotId,
            normalizedSource,
            revision,
            storedPayload,
            CollectNodeIds(storedPayload));

        lock (gate)
        {
            snapshots[snapshotId] = snapshot;
            latestRevisions[normalizedSource] = revision;
            insertionOrder.Enqueue(snapshotId);
            while (insertionOrder.Count > MaximumSnapshots)
            {
                snapshots.Remove(insertionOrder.Dequeue());
            }
        }

        return ToolResult.Success(payload, result.Message);
    }

    internal static bool TryGet(
        string snapshotId,
        string? source,
        bool requireCurrent,
        out VisualTreeSnapshot? snapshot,
        out ToolResult? error)
    {
        lock (gate)
        {
            if (!snapshots.TryGetValue(snapshotId, out snapshot))
            {
                var requestedSource = VisualTreeProviderRegistry.NormalizeSourceOrDefault(source);
                error = CreateStaleError(
                    snapshotId,
                    requestedSource,
                    "The referenced visual-tree snapshot is unknown or has expired.");
                return false;
            }

            var normalizedSource = string.IsNullOrWhiteSpace(source)
                ? snapshot.Source
                : VisualTreeProviderRegistry.NormalizeSourceOrDefault(source);

            if (!string.Equals(snapshot.Source, normalizedSource, StringComparison.OrdinalIgnoreCase))
            {
                error = CreateStaleError(
                    snapshotId,
                    normalizedSource,
                    $"Snapshot '{snapshotId}' belongs to source '{snapshot.Source}', not '{normalizedSource}'.");
                snapshot = null;
                return false;
            }

            if (requireCurrent
                && latestRevisions.TryGetValue(normalizedSource, out var latestRevision)
                && latestRevision != snapshot.Revision)
            {
                error = CreateStaleError(
                    snapshotId,
                    normalizedSource,
                    $"Snapshot '{snapshotId}' was superseded by revision {latestRevision}.",
                    latestRevision);
                snapshot = null;
                return false;
            }

            error = null;
            return true;
        }
    }

    internal static bool TryValidateNodeReference(
        string snapshotId,
        string? source,
        string nodeId,
        out VisualTreeSnapshot? snapshot,
        out ToolResult? error)
    {
        if (!TryGet(snapshotId, source, requireCurrent: true, out snapshot, out error))
        {
            return false;
        }

        if (!snapshot!.NodeIds.Contains(nodeId))
        {
            error = CreateStaleError(
                snapshotId,
                snapshot.Source,
                $"Node '{nodeId}' does not belong to snapshot '{snapshotId}'.",
                snapshot.Revision,
                nodeId);
            snapshot = null;
            return false;
        }

        return true;
    }

    internal static JsonObject CreateReference(VisualTreeSnapshot snapshot, string nodeId)
        => new()
        {
            ["source"] = snapshot.Source,
            ["snapshotId"] = snapshot.SnapshotId,
            ["revision"] = snapshot.Revision,
            ["nodeId"] = nodeId
        };

    private static HashSet<string> CollectNodeIds(JsonObject payload)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (payload["root"] is JsonObject root)
        {
            CollectNodeIds(root, nodeIds);
        }

        return nodeIds;
    }

    private static void CollectNodeIds(JsonObject node, HashSet<string> nodeIds)
    {
        if (node["id"]?.GetValue<string>() is { Length: > 0 } nodeId)
        {
            nodeIds.Add(nodeId);
        }

        if (node["children"] is not JsonArray children)
        {
            return;
        }

        foreach (var child in children.OfType<JsonObject>())
        {
            CollectNodeIds(child, nodeIds);
        }
    }

    private static ToolResult CreateStaleError(
        string snapshotId,
        string source,
        string message,
        long? latestRevision = null,
        string? nodeId = null)
        => ToolResult.Failure(
            message,
            errorCode: "stale_node_reference",
            payload: new JsonObject
            {
                ["source"] = source,
                ["snapshotId"] = snapshotId,
                ["nodeId"] = nodeId,
                ["latestRevision"] = latestRevision,
                ["refreshWith"] = VisualTreeToolIds.QueryNodes
            });
}

internal sealed record VisualTreeSnapshot(
    string SnapshotId,
    string Source,
    long Revision,
    JsonObject Payload,
    IReadOnlySet<string> NodeIds);
