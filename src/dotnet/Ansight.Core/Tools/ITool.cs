namespace Ansight.Tools;

/// <summary>
/// Contract implemented by a remotely invokable Ansight tool.
/// </summary>
public interface ITool
{
    /// <summary>
    /// High-level category name used to group the tool in catalogs and client UIs.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Access scope required to discover and execute the tool.
    /// </summary>
    ToolScope Scope { get; }

    /// <summary>
    /// Stable unique identifier used to invoke the tool over the protocol.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable tool name shown in catalogs and client UIs.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of what the tool does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Search keywords that help clients discover the tool.
    /// </summary>
    string Keywords { get; }

    /// <summary>
    /// Schema describing the tool's flattened string arguments.
    /// </summary>
    ToolSchema ArgumentsSchema { get; }

    /// <summary>
    /// Schema describing the JSON result payload returned by the tool.
    /// </summary>
    ToolSchema ResultSchema { get; }

    /// <summary>
    /// Optional security metadata describing the sensitivity of the tool.
    /// </summary>
    ToolSecurity Security => ToolSecurity.Unspecified;

    /// <summary>
    /// Convenience metadata record built from the tool's public properties.
    /// </summary>
    ToolDefinition Definition => new(
        Id,
        Name,
        Description,
        Category,
        Scope,
        Keywords,
        ArgumentsSchema,
        ResultSchema)
    {
        Security = Security
    };

    /// <summary>
    /// Evaluates whether the tool can execute in the app's current runtime state.
    /// </summary>
    /// <param name="context">Current tool protocol session and request context.</param>
    /// <returns>Structured runtime availability. Tools without preconditions are available by default.</returns>
    ValueTask<ToolAvailability> GetAvailabilityAsync(ToolAvailabilityContext context)
        => ValueTask.FromResult(ToolAvailability.Available);

    /// <summary>
    /// Executes the tool using flattened string arguments derived from the incoming protocol payload.
    /// </summary>
    /// <param name="arguments">Tool arguments keyed by argument name.</param>
    /// <returns>A structured tool result describing success, failure, and optional JSON payload data.</returns>
    Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments);
}
