namespace Ansight.Tools.VisualTree;

public sealed class InspectNodeTool : ITool
{
    public string Category => "ui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => VisualTreeToolIds.InspectNode;

    public string Name => "Inspect Node";

    public string Description => "Returns detailed metadata for a visual tree node.";

    public string Keywords => "ui node inspect accessibility layout";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.InspectNodeArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.InspectNodeResult;

    public ToolSecurity Security => VisualTreeToolSecurityProfiles.InspectNode;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.InspectNodeAsync(arguments);
    }
}
