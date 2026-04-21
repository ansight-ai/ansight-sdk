namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetElementTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetElement;

    public string Name => "Get Element";

    public string Description => "Returns a focused diagnostic snapshot for one node in the current .NET MAUI visual tree.";

    public string Keywords => "maui element inspect details ancestors children properties";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetElementArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.ElementResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetElement;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var includeBounds = GetBoolean(arguments, "includeBounds", defaultValue: true);
            var includeProperties = GetBoolean(arguments, "includeProperties", defaultValue: true);
            var includeBindableProperties = GetBoolean(arguments, "includeBindableProperties", defaultValue: true);
            var includeBindingContext = GetBoolean(arguments, "includeBindingContext", defaultValue: true);
            var includeChildren = GetBoolean(arguments, "includeChildren", defaultValue: true);

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            var element = resolution.Element;
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["root"] = CreateElementReference(resolution.Root),
                ["node"] = CreateElementReference(element),
                ["path"] = CreateElementPath(resolution.Ancestors, element),
                ["parent"] = element.Parent == null ? null : CreateElementReference(element.Parent)
            };

            if (element is VisualElement visualElement)
            {
                payload["visible"] = visualElement.IsVisible;
                payload["enabled"] = visualElement.IsEnabled;

                if (includeBounds)
                {
                    payload["bounds"] = CreateBoundsSnapshot(visualElement);
                }
            }

            var children = GetChildElements(element);
            payload["childCount"] = children.Count;
            if (includeChildren)
            {
                payload["children"] = CreateElementReferenceArray(children);
            }

            if (includeProperties)
            {
                payload["properties"] = CreateElementProperties(element);
            }

            if (includeBindableProperties && element is BindableObject bindable)
            {
                payload["bindableProperties"] = CreateBindablePropertiesArray(bindable);
            }

            if (includeBindingContext)
            {
                payload["hasBindingContext"] = element.BindingContext != null;
                payload["bindingContextType"] = element.BindingContext == null ? null : CreateTypeMetadata(element.BindingContext.GetType());
            }

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
