namespace Ansight.Tools.VisualTree;

public sealed class ShowOverlayTool : ITool
{
    public string Category => "ui";

    public ToolPolicy Policy => ToolPolicy.Write;

    public string Id => VisualTreeToolIds.ShowOverlay;

    public string Name => "Show Overlay";

    public string Description => "Draws an input-transparent diagnostic overlay over the active app window.";

    public string Keywords => "ui overlay highlight box rectangle diagnostic";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.ShowOverlayArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.OverlayResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.ShowOverlayAsync(arguments);
    }
}
