namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Guard policy that controls whether registered tools may be discovered and executed.
/// </summary>
public sealed class ToolGuard
{
    private readonly HashSet<ToolScope> allowedScopes;
    private readonly HashSet<string> allowedToolIds;

    /// <summary>
    /// Guard preset that disables both tool discovery and execution.
    /// </summary>
    public static ToolGuard Disabled { get; } = new(discoveryEnabled: false, executionEnabled: false, Array.Empty<ToolScope>(), Array.Empty<string>());

    /// <summary>
    /// Guard preset that allows discovery and execution for read-scoped tools only.
    /// </summary>
    public static ToolGuard ReadOnly { get; } = new(discoveryEnabled: true, executionEnabled: true, [ToolScope.Read], Array.Empty<string>());

    /// <summary>
    /// Guard preset that allows discovery and execution for read- and write-scoped tools.
    /// </summary>
    public static ToolGuard ReadWrite { get; } = new(
        discoveryEnabled: true,
        executionEnabled: true,
        new[] { ToolScope.Read, ToolScope.Write },
        Array.Empty<string>());

    /// <summary>
    /// Guard preset that allows discovery and execution for every tool scope.
    /// </summary>
    public static ToolGuard FullAccess { get; } = new(
        discoveryEnabled: true,
        executionEnabled: true,
        Enum.GetValues<ToolScope>(),
        Array.Empty<string>());

    /// <summary>
    /// Creates a tool guard with explicit discovery, execution, scope, and per-tool rules.
    /// </summary>
    /// <param name="discoveryEnabled"><see langword="true"/> to include allowed tools in discovery catalogs.</param>
    /// <param name="executionEnabled"><see langword="true"/> to allow execution of allowed tools.</param>
    /// <param name="allowedScopes">Tool scopes enabled by this guard.</param>
    /// <param name="allowedToolIds">Optional allow-list of tool ids. When empty, all tools in the allowed scopes are eligible.</param>
    public ToolGuard(
        bool discoveryEnabled,
        bool executionEnabled,
        IEnumerable<ToolScope> allowedScopes,
        IEnumerable<string>? allowedToolIds = null)
    {
        ArgumentNullException.ThrowIfNull(allowedScopes);

        DiscoveryEnabled = discoveryEnabled;
        ExecutionEnabled = executionEnabled;
        this.allowedScopes = new HashSet<ToolScope>(allowedScopes);
        this.allowedToolIds = new HashSet<string>(allowedToolIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
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
    /// Tool scopes enabled by this guard.
    /// </summary>
    public IReadOnlyCollection<ToolScope> AllowedScopes => allowedScopes;

    /// <summary>
    /// Optional allow-list of tool ids enabled by this guard.
    /// </summary>
    public IReadOnlyCollection<string> AllowedToolIds => allowedToolIds;

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

        if (!allowedScopes.Contains(tool.Scope))
        {
            reason = $"Tool scope '{tool.Scope}' is not enabled by the current guard policy.";
            return false;
        }

        if (allowedToolIds.Count > 0 && !allowedToolIds.Contains(tool.Id))
        {
            reason = $"Tool '{tool.Id}' is not allowed by the current guard policy.";
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
        var scopes = new JsonArray();
        foreach (var scope in allowedScopes.OrderBy(scope => scope))
        {
            scopes.Add(scope.ToString());
        }

        var toolIds = new JsonArray();
        foreach (var toolId in allowedToolIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            toolIds.Add(toolId);
        }

        return new JsonObject
        {
            ["discoveryEnabled"] = DiscoveryEnabled,
            ["executionEnabled"] = ExecutionEnabled,
            ["allowedScopes"] = scopes,
            ["allowedToolIds"] = toolIds
        };
    }

    /// <summary>
    /// Validates that the guard configuration is internally consistent.
    /// </summary>
    public void Validate()
    {
        if (ExecutionEnabled && allowedScopes.Count == 0)
        {
            throw new InvalidOperationException("Tool execution cannot be enabled without at least one allowed scope.");
        }
    }

    private bool IsToolAllowed(ITool tool)
    {
        if (!allowedScopes.Contains(tool.Scope))
        {
            return false;
        }

        return allowedToolIds.Count == 0 || allowedToolIds.Contains(tool.Id);
    }
}
