namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetBindablePropertyTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => MauiToolIds.GetBindableProperty;

    public string Name => "Get Bindable Property";

    public string Description => "Reads a bindable property from a node in the current .NET MAUI visual tree.";

    public string Keywords => "maui bindable property get read binding value xaml";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetBindablePropertyArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindablePropertyResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var propertyName = GetRequiredString(arguments, "propertyName");
            var declaringTypeName = GetString(arguments, "declaringTypeName");

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            if (resolution.Element is not BindableObject bindable)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' is not a BindableObject.", errorCode: "maui_node_not_bindable");
            }

            var descriptor = ResolveBindableProperty(bindable, propertyName, declaringTypeName);
            if (descriptor == null)
            {
                return ToolResult.Failure($"The bindable property '{propertyName}' was not found on node '{nodeId}'.", errorCode: "maui_bindable_property_not_found");
            }

            var value = bindable.GetValue(descriptor.BindableProperty);
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["property"] = CreateBindablePropertyMetadata(bindable, descriptor),
                ["binding"] = CreateBindingInfo(bindable, descriptor.BindableProperty),
                ["value"] = CreateValueSnapshot(value, descriptor.BindableProperty.ReturnType, DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties)
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
