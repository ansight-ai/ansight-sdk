namespace Ansight.Tools;

using System.Text.Json.Nodes;

public sealed class ToolGuard
{
    private readonly HashSet<ToolScope> allowedScopes;
    private readonly HashSet<string> allowedToolIds;

    public static ToolGuard Disabled { get; } = new(discoveryEnabled: false, executionEnabled: false, Array.Empty<ToolScope>(), Array.Empty<string>());

    public static ToolGuard ReadOnly { get; } = new(discoveryEnabled: true, executionEnabled: true, [ToolScope.Read], Array.Empty<string>());

    public static ToolGuard FullAccess { get; } = new(
        discoveryEnabled: true,
        executionEnabled: true,
        Enum.GetValues<ToolScope>(),
        Array.Empty<string>());

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

    public bool DiscoveryEnabled { get; }

    public bool ExecutionEnabled { get; }

    public IReadOnlyCollection<ToolScope> AllowedScopes => allowedScopes;

    public IReadOnlyCollection<string> AllowedToolIds => allowedToolIds;

    public bool IsToolVisible(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return DiscoveryEnabled && IsToolAllowed(tool);
    }

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
