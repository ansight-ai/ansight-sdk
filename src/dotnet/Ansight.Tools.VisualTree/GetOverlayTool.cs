namespace Ansight.Tools.VisualTree;

public sealed class GetOverlayTool : ITool
{
    public string Category => "ui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => VisualTreeToolIds.GetOverlay;

    public string Name => "Get Overlay";

    public string Description => "Returns metadata and geometry for a live diagnostic overlay.";

    public string Keywords => "ui overlay highlight inspect metadata";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.GetOverlayArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.OverlayResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.GetOverlayAsync(arguments);
    }
}
