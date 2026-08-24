namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class AddElementTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => MauiToolIds.AddElement;

    public string Name => "Add Element";

    public string Description => "Adds an inflated or existing .NET MAUI element to the live visual tree.";

    public string Keywords => "maui add element visual tree attach control experiment";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.AddElementArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.ElementTreeMutationResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var parentNodeId = GetRequiredString(arguments, "parentNodeId");
            var elementNodeId = GetRequiredString(arguments, "elementNodeId");
            var index = GetOptionalInt(arguments, "index");
            var replaceContent = GetBoolean(arguments, "replaceContent", defaultValue: false);
            var detachFromCurrentParent = GetBoolean(arguments, "detachFromCurrentParent", defaultValue: false);

            var parentResolution = ResolveElement(parentNodeId);
            if (parentResolution == null)
            {
                return ToolResult.Failure($"The MAUI parent node '{parentNodeId}' was not found.", errorCode: "maui_parent_node_not_found");
            }

            var elementResolution = ResolveElementOrInflated(elementNodeId);
            if (elementResolution == null)
            {
                return ToolResult.Failure($"The MAUI element node '{elementNodeId}' was not found.", errorCode: "maui_element_node_not_found");
            }

            var parent = parentResolution.Element;
            var element = elementResolution.Element;
            if (ReferenceEquals(parent, element))
            {
                return ToolResult.Failure("An element cannot be added to itself.", errorCode: "maui_element_cannot_parent_self");
            }

            if (parentResolution.Ancestors.Any(ancestor => ReferenceEquals(ancestor, element)))
            {
                return ToolResult.Failure("An element cannot be added under one of its own descendants.", errorCode: "maui_element_cannot_parent_descendant");
            }

            if (element.Parent != null)
            {
                if (ReferenceEquals(element.Parent, parent) && !detachFromCurrentParent)
                {
                    var existingPayload = new JsonObject
                    {
                        ["platform"] = CurrentPlatform,
                        ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                        ["parent"] = CreateElementReference(parent),
                        ["node"] = CreateElementReference(element),
                        ["container"] = null,
                        ["added"] = false,
                        ["alreadyParented"] = true,
                        ["childCount"] = GetChildElements(parent).Count
                    };

                    return ToolResult.Success(existingPayload);
                }

                if (!detachFromCurrentParent)
                {
                    return ToolResult.Failure("The element already has a parent. Pass detachFromCurrentParent=true to move it.", errorCode: "maui_element_already_parented");
                }

                if (!TryDetachElement(element, out _, out _, out var detachError))
                {
                    return ToolResult.Failure(detachError ?? "The element could not be detached from its current parent.", errorCode: "maui_element_detach_failed");
                }
            }

            if (!TryAttachElement(parent, element, index, replaceContent, out var container, out var attachError))
            {
                return ToolResult.Failure(attachError ?? "The element could not be attached to the requested parent.", errorCode: "maui_element_attach_failed");
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["parent"] = CreateElementReference(parent),
                ["node"] = CreateElementReference(element),
                ["container"] = container,
                ["added"] = true,
                ["childCount"] = GetChildElements(parent).Count
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
