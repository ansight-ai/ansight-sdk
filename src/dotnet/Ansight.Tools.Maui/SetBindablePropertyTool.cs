namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class SetBindablePropertyTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => MauiToolIds.SetBindableProperty;

    public string Name => "Set MAUI Bindable Property";

    public string Description => "Writes a bindable property on a node in the current .NET MAUI visual tree.";

    public string Keywords => "maui bindable property set write mutate binding value xaml";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.SetBindablePropertyArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindablePropertyMutationResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.SetBindableProperty;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var propertyName = GetRequiredString(arguments, "propertyName");
            var valueJson = GetRequiredString(arguments, "valueJson");
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

            object? convertedValue;
            try
            {
                convertedValue = ConvertJsonArgument(valueJson, descriptor.BindableProperty.ReturnType);
            }
            catch (Exception exception)
            {
                return ToolResult.Failure(exception.Message, errorCode: "maui_bindable_value_conversion_failed");
            }

            try
            {
                bindable.SetValue(descriptor.BindableProperty, convertedValue);
            }
            catch (Exception exception)
            {
                return ToolResult.Failure(exception.Message, errorCode: "maui_bindable_property_write_failed");
            }

            var updatedValue = bindable.GetValue(descriptor.BindableProperty);
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["property"] = CreateBindablePropertyMetadata(bindable, descriptor),
                ["binding"] = CreateBindingInfo(bindable, descriptor.BindableProperty),
                ["updated"] = true,
                ["value"] = CreateValueSnapshot(updatedValue, descriptor.BindableProperty.ReturnType, DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties)
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
