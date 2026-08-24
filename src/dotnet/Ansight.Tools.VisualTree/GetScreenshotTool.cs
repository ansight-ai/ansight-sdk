namespace Ansight.Tools.VisualTree;

public sealed class GetScreenshotTool : ITool
{
    public string Category => "ui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => VisualTreeToolIds.GetScreenshot;

    public string Name => "Get Screenshot";

    public string Description => "Captures a screenshot of the current app scene.";

    public string Keywords => "ui screenshot image capture";

    public ToolSchema ArgumentsSchema => VisualTreeToolSchemas.GetScreenshotArguments;

    public ToolSchema ResultSchema => VisualTreeToolSchemas.ScreenshotResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.GetScreenshotAsync(arguments);
    }
}
