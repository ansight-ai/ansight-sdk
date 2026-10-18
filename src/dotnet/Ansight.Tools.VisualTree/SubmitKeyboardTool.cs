namespace Ansight.Tools.VisualTree;

/// <summary>Invokes the focused editor's configured action without injecting a newline.</summary>
public sealed class SubmitKeyboardTool : ITool
{
    public string Category => "ui";
    public ToolPolicy Policy => ToolPolicy.Write;
    public string Id => VisualTreeToolIds.SubmitKeyboard;
    public string Name => "Submit Keyboard";
    public string Description => "Invokes the focused input's configured editor action. Fails if no submit action is available.";
    public string Keywords => "ui keyboard ime submit return done search go send";
    public ToolSchema ArgumentsSchema => ToolSchema.Object(additionalProperties: false);
    public ToolSchema ResultSchema => ToolSchema.Object(properties: new Dictionary<string, ToolSchema>
    {
        ["performed"] = ToolSchema.Boolean("Whether the editor action was dispatched; verify the app's resulting state.")
    }, required: ["performed"], additionalProperties: false);

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
        => VisualTreeSupport.SubmitKeyboardAsync();
}
