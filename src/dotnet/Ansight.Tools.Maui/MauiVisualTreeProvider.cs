namespace Ansight.Tools.Maui;

using Ansight.Tools.VisualTree;

/// <summary>
/// Supplies the logical .NET MAUI visual tree to local capture and remote tools.
/// </summary>
public sealed class MauiVisualTreeProvider : IVisualTreeProvider, IVisualTreeInteractionProvider
{
    /// <summary>
    /// Source identifier used for MAUI visual-tree capture.
    /// </summary>
    public const string SourceId = "maui";

    /// <summary>
    /// Shared stateless provider instance.
    /// </summary>
    public static MauiVisualTreeProvider Instance { get; } = new();

    private MauiVisualTreeProvider()
    {
    }

    public string Source => SourceId;

    public string DisplayName => ".NET MAUI";

    public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
        => GetVisualTreeTool.CaptureAsync(arguments);

    public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
        => new GetElementTool().Execute(arguments);

    public async Task<ToolResult> PerformActionAsync(
        VisualTreeActionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var action = request.Action.Trim().ToLowerInvariant() switch
        {
            "tap" => "invokeTap",
            "setvalue" or "typetext" => "setText",
            "select" => "selectPickerItem",
            "selecttab" => "selectTab",
            var value => value
        };
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = request.NodeId,
            ["action"] = action
        };
        if (request.Value is not null)
        {
            arguments["valueJson"] = request.Value.ToJsonString();
        }

        foreach (var option in request.Options)
        {
            if (option.Value is not null)
            {
                arguments[option.Key] = option.Value is System.Text.Json.Nodes.JsonValue value
                    ? value.ToString()
                    : option.Value.ToJsonString();
            }
        }

        var result = await new InvokeElementActionTool().Execute(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
