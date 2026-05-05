namespace Ansight.Tools.VisualTree;

public sealed class UpdateOverlayTool : ITool
{
    public string Category => "ui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => VisualTreeToolIds.UpdateOverlay;

    public string Name => "Update Overlay";

    public string Description => "Edits an existing input-transparent diagnostic overlay.";

    public string Keywords => "ui overlay highlight update edit mutate";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.UpdateOverlayArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.OverlayResult;

    public ToolSecurity Security => VisualTreeToolSecurityProfiles.UpdateOverlay;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.UpdateOverlayAsync(arguments);
    }
}
