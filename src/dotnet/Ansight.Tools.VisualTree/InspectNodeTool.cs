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

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        arguments.TryGetValue("source", out var source);
        if (!VisualTreeProviderRegistry.TryGet(source, out var provider) || provider is null)
        {
            var normalizedSource = VisualTreeProviderRegistry.NormalizeSourceOrDefault(source);
            return ToolResult.Failure(
                $"No visual tree provider is registered for source '{normalizedSource}'.",
                errorCode: "visual_tree_provider_not_found");
        }

        return await provider.InspectNodeAsync(arguments);
    }
}
