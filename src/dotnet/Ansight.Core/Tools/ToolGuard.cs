namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Guard policy that controls whether registered tools may be discovered and executed.
/// </summary>
public sealed class ToolGuard
{
    /// <summary>No access by default; package opt-ins may grant exact tools.</summary>
    internal static ToolGuard Unconfigured { get; } = new(false, false, ToolPolicy.Read)
    {
        permitsDefaultPolicy = false,
        permitsScopedActivation = true
    };

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

    private bool permitsDefaultPolicy = true;
    private bool permitsScopedActivation;
    private readonly Dictionary<string, ToolPolicy> toolGrants = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Authorizes one exact tool up to a policy; global disable still wins.</summary>
    public ToolGuard Granting(string toolId, ToolPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        var copy = new ToolGuard(DiscoveryEnabled || permitsScopedActivation, ExecutionEnabled || permitsScopedActivation, MaxPolicy)
        {
            permitsDefaultPolicy = permitsDefaultPolicy,
            permitsScopedActivation = permitsScopedActivation
        };
        foreach (var grant in toolGrants) copy.toolGrants.Add(grant.Key, grant.Value);
        copy.toolGrants[toolId.Trim()] = policy;
        return copy;
    }

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

        if (!IsToolAllowed(tool))
        {
            reason = $"Tool '{tool.Id}' is not authorized at policy '{tool.Policy}' by the current guard.";
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
            ["maxPolicy"] = MaxPolicy.ToString().ToLowerInvariant(),
            ["toolGrants"] = new JsonObject(toolGrants.OrderBy(grant => grant.Key, StringComparer.OrdinalIgnoreCase)
                .Select(grant => new KeyValuePair<string, JsonNode?>(grant.Key, JsonValue.Create(grant.Value.ToString().ToLowerInvariant()))))
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
        return (permitsDefaultPolicy && tool.Policy <= MaxPolicy)
            || (toolGrants.TryGetValue(tool.Id.Trim(), out var grant) && tool.Policy <= grant);
    }
}
