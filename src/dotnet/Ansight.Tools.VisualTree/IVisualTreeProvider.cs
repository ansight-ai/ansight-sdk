namespace Ansight.Tools.VisualTree;

/// <summary>
/// Supplies a visual tree for one UI framework or rendering source.
/// </summary>
public interface IVisualTreeProvider
{
    /// <summary>
    /// Stable, case-insensitive source identifier such as <c>native</c> or <c>maui</c>.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// Human-readable provider name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Captures the provider's current visual tree.
    /// </summary>
    Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments);

    /// <summary>
    /// Inspects one node in the provider's current visual tree.
    /// </summary>
    Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments);
}
