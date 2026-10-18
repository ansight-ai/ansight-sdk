namespace Ansight.Tools.VisualTree;

/// <summary>Dismisses the app's input method without injecting navigation keys.</summary>
public sealed class DismissKeyboardTool : ITool
{
    public string Category => "ui";
    public ToolPolicy Policy => ToolPolicy.Write;
    public string Id => VisualTreeToolIds.DismissKeyboard;
    public string Name => "Dismiss Keyboard";
    public string Description => "Requests dismissal of the software keyboard in the foreground app without navigating back.";
    public string Keywords => "ui keyboard ime dismiss hide";
    public ToolSchema ArgumentsSchema => ToolSchema.Object(additionalProperties: false);
    public ToolSchema ResultSchema => ToolSchema.Object(
        properties: new Dictionary<string, ToolSchema>
        {
            ["requested"] = ToolSchema.Boolean("Whether keyboard dismissal was requested. Verify visibility after the platform applies it.")
        },
        required: ["requested"],
        additionalProperties: false);

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return VisualTreeSupport.DismissKeyboardAsync();
    }
}
