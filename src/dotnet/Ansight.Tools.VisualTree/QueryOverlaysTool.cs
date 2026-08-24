namespace Ansight.Tools.VisualTree;

public sealed class QueryOverlaysTool : ITool
{
    public string Category => "ui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => VisualTreeToolIds.QueryOverlays;

    public string Name => "Query Overlays";

    public string Description => "Lists live diagnostic overlays and supports simple metadata filtering.";

    public string Keywords => "ui overlay highlight query list metadata";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.QueryOverlaysArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.QueryOverlaysResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.QueryOverlaysAsync(arguments);
    }
}
