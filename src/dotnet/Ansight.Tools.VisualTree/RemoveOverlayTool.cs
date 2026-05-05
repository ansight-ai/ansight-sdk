namespace Ansight.Tools.VisualTree;

public sealed class RemoveOverlayTool : ITool
{
    public string Category => "ui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => VisualTreeToolIds.RemoveOverlay;

    public string Name => "Remove Overlay";

    public string Description => "Removes a diagnostic overlay from the active app window by id.";

    public string Keywords => "ui overlay highlight remove clear";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.RemoveOverlayArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.RemoveOverlayResult;

    public ToolSecurity Security => VisualTreeToolSecurityProfiles.RemoveOverlay;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.RemoveOverlayAsync(arguments);
    }
}
