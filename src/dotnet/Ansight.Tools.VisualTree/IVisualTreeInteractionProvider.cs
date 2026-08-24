namespace Ansight.Tools.VisualTree;

using System.Text.Json.Nodes;

/// <summary>
/// Optional provider contract for framework-neutral UI actions.
/// </summary>
public interface IVisualTreeInteractionProvider
{
    /// <summary>
    /// Performs an action against a node resolved from a current visual-tree snapshot.
    /// </summary>
    Task<ToolResult> PerformActionAsync(
        VisualTreeActionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Framework-neutral UI action request.
/// </summary>
public sealed record VisualTreeActionRequest(
    string NodeId,
    string Action,
    JsonNode? Value,
    JsonObject Options);
