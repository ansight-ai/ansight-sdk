namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Guard policy that controls whether registered tools may be discovered and executed.
/// </summary>
public sealed class ToolGuard
{
    /// <summary>
    /// Guard preset that disables both tool discovery and execution.
    /// </summary>
    public static ToolGuard Disabled { get; } = new(
        discoveryEnabled: false,
        executionEnabled: false,
        ToolPolicy.Read);

    /// <summary>
    /// Guard preset that allows discovery and execution for read-policy tools only.
    /// </summary>
    public static ToolGuard ReadOnly { get; } = new(
        discoveryEnabled: true,
        executionEnabled: true,
        ToolPolicy.Read);

    /// <summary>
    /// Guard preset that allows discovery and execution through write policy.
    /// </summary>
    public static ToolGuard ReadWrite { get; } = new(
        discoveryEnabled: true,
        executionEnabled: true,
        ToolPolicy.Write);

    /// <summary>
    /// Guard preset that allows discovery and execution through critical policy.
    /// </summary>
    public static ToolGuard FullAccess { get; } = new(
        discoveryEnabled: true,
        executionEnabled: true,
        ToolPolicy.Critical);

    /// <summary>
    /// Creates a tool guard with explicit discovery, execution, and maximum policy.
    /// </summary>
    /// <param name="discoveryEnabled"><see langword="true"/> to include allowed tools in discovery catalogs.</param>
    /// <param name="executionEnabled"><see langword="true"/> to allow execution of allowed tools.</param>
    /// <param name="maxPolicy">Highest tool policy enabled by this guard.</param>
    public ToolGuard(
        bool discoveryEnabled,
        bool executionEnabled,
        ToolPolicy maxPolicy)
    {
        DiscoveryEnabled = discoveryEnabled;
        ExecutionEnabled = executionEnabled;
        MaxPolicy = maxPolicy;
    }

    /// <summary>
    /// Indicates whether tool discovery is enabled.
    /// </summary>
    public bool DiscoveryEnabled { get; }

    /// <summary>
    /// Indicates whether tool execution is enabled.
    /// </summary>
    public bool ExecutionEnabled { get; }

    /// <summary>
    /// Highest tool policy enabled by this guard.
    /// </summary>
    public ToolPolicy MaxPolicy { get; }

    /// <summary>
    /// Determines whether a tool should appear in discovery results under this guard.
    /// </summary>
    /// <param name="tool">Tool to evaluate.</param>
    /// <returns><see langword="true"/> when the tool is visible in discovery catalogs; otherwise, <see langword="false"/>.</returns>
    public bool IsToolVisible(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return DiscoveryEnabled && IsToolAllowed(tool);
    }

    /// <summary>
    /// Determines whether a tool may be executed under this guard.
    /// </summary>
    /// <param name="tool">Tool to evaluate.</param>
    /// <param name="reason">Human-readable denial reason when execution is not allowed.</param>
    /// <returns><see langword="true"/> when execution is allowed; otherwise, <see langword="false"/>.</returns>
    public bool CanExecute(ITool tool, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (!ExecutionEnabled)
        {
            reason = "Tool execution is disabled by the current guard policy.";
            return false;
        }

        if (tool.Policy > MaxPolicy)
        {
            reason = $"Tool policy '{tool.Policy}' exceeds the current '{MaxPolicy}' grant.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Converts the guard into a JSON object suitable for protocol payloads and diagnostics.
    /// </summary>
    /// <returns>JSON representation of the guard.</returns>
    public JsonObject ToJson()
    {
        return new JsonObject
        {
            ["discoveryEnabled"] = DiscoveryEnabled,
            ["executionEnabled"] = ExecutionEnabled,
            ["maxPolicy"] = MaxPolicy.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    /// Validates that the guard configuration is internally consistent.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(MaxPolicy))
        {
            throw new InvalidOperationException("The maximum tool policy is invalid.");
        }
    }

    private bool IsToolAllowed(ITool tool)
    {
        return tool.Policy <= MaxPolicy;
    }
}
