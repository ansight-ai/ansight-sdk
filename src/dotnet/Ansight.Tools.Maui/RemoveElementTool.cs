namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class RemoveElementTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => MauiToolIds.RemoveElement;

    public string Name => "Remove Element";

    public string Description => "Removes an inflated or existing .NET MAUI element from the live visual tree.";

    public string Keywords => "maui remove element visual tree detach control experiment";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.RemoveElementArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.ElementTreeMutationResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var forget = GetBoolean(arguments, "forget", defaultValue: false);

            var resolution = ResolveElementOrInflated(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI element node '{nodeId}' was not found.", errorCode: "maui_element_node_not_found");
            }

            var element = resolution.Element;
            if (!TryDetachElement(element, out var parent, out var container, out var detachError))
            {
                return ToolResult.Failure(detachError ?? "The element could not be detached from its current parent.", errorCode: "maui_element_detach_failed");
            }

            var forgot = forget && ForgetInflatedElement(element);
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["parent"] = parent == null ? null : CreateElementReference(parent),
                ["node"] = CreateElementReference(element),
                ["container"] = container,
                ["removed"] = parent != null,
                ["forgot"] = forgot,
                ["parentChildCount"] = parent == null ? null : GetChildElements(parent).Count
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
