namespace Ansight.Tools.VisualTree;

public sealed class ClearOverlaysTool : ITool
{
    public string Category => "ui";

    public ToolPolicy Policy => ToolPolicy.Write;

    public string Id => VisualTreeToolIds.ClearOverlays;

    public string Name => "Clear Overlays";

    public string Description => "Removes all diagnostic overlays from the active app window.";

    public string Keywords => "ui overlay highlight clear remove all";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.ClearOverlaysArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.ClearOverlaysResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.ClearOverlaysAsync(arguments);
    }
}
