namespace Ansight.Tools;

/// <summary>
/// Serializable metadata that describes a tool for discovery and protocol catalogs.
/// </summary>
/// <param name="Id">Stable unique identifier used to invoke the tool.</param>
/// <param name="Name">Human-readable tool name.</param>
/// <param name="Description">Human-readable description of the tool.</param>
/// <param name="Category">High-level category name used to group the tool.</param>
/// <param name="Scope">Access scope required to discover and execute the tool.</param>
/// <param name="Keywords">Search keywords that help clients discover the tool.</param>
/// <param name="ArgumentsSchema">Schema describing the tool's flattened string arguments.</param>
/// <param name="ResultSchema">Schema describing the tool's JSON result payload.</param>
public sealed record ToolDefinition(
    string Id,
    string Name,
    string Description,
    string Category,
    ToolScope Scope,
    string Keywords,
    ToolSchema ArgumentsSchema,
    ToolSchema ResultSchema)
{
    /// <summary>
    /// Optional security metadata describing the sensitivity of the tool.
    /// </summary>
    public ToolSecurity Security { get; init; } = ToolSecurity.Unspecified;
}
