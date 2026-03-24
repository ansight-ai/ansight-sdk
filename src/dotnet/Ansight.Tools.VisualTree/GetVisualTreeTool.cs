namespace Ansight.Tools.VisualTree;

public sealed class GetVisualTreeTool : ITool
{
    public string Category => "ui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => VisualTreeToolIds.GetVisualTree;

    public string Name => "Get Visual Tree";

    public string Description => "Returns the current UI hierarchy for the foreground scene.";

    public string Keywords => "ui visual tree hierarchy layout";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.GetVisualTreeArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.VisualTreeResult;

    public ToolSecurity Security => VisualTreeToolSecurityProfiles.GetVisualTree;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.GetVisualTreeAsync(arguments);
    }
}
