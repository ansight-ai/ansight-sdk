namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class ClearBindablePropertyTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Write;

    public string Id => MauiToolIds.ClearBindableProperty;

    public string Name => "Clear MAUI Bindable Property";

    public string Description => "Clears a local value or binding from a bindable property on a node in the current .NET MAUI visual tree.";

    public string Keywords => "maui bindable property clear reset local value binding";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.ClearBindablePropertyArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindablePropertyMutationResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.ClearBindableProperty;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var propertyName = GetRequiredString(arguments, "propertyName");
            var declaringTypeName = GetString(arguments, "declaringTypeName");
            var mode = (GetString(arguments, "mode") ?? "both").Trim();

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

            var normalizedMode = mode.ToLowerInvariant();
            if (normalizedMode is not ("value" or "binding" or "both"))
            {
                return ToolResult.Failure("The mode argument must be one of: value, binding, both.", errorCode: "maui_invalid_clear_mode");
            }

            var shouldClearBinding = normalizedMode is "binding" or "both";
            var shouldClearValue = normalizedMode is "value" or "both";
            var hadBinding = GetBinding(bindable, descriptor.BindableProperty) != null;
            var hadLocalValue = IsBindablePropertySet(bindable, descriptor.BindableProperty);
            var removedBinding = false;
            var clearedValue = false;

            try
            {
                if (shouldClearBinding)
                {
                    removedBinding = RemoveBinding(bindable, descriptor.BindableProperty);
                    if (!removedBinding)
                    {
                        return ToolResult.Failure("The binding could not be removed from the bindable property.", errorCode: "maui_bindable_property_binding_remove_failed");
                    }
                }

                if (shouldClearValue)
                {
                    bindable.ClearValue(descriptor.BindableProperty);
                    clearedValue = true;
                }
            }
            catch (Exception exception)
            {
                return ToolResult.Failure(exception.Message, errorCode: "maui_bindable_property_clear_failed");
            }

            var updatedValue = bindable.GetValue(descriptor.BindableProperty);
            var hasBinding = GetBinding(bindable, descriptor.BindableProperty) != null;
            var updated = (shouldClearBinding && hadBinding && !hasBinding) ||
                          (shouldClearValue && hadLocalValue);
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["property"] = CreateBindablePropertyMetadata(bindable, descriptor),
                ["binding"] = CreateBindingInfo(bindable, descriptor.BindableProperty),
                ["updated"] = updated,
                ["mode"] = normalizedMode,
                ["hadBinding"] = hadBinding,
                ["removedBinding"] = removedBinding,
                ["hasBinding"] = hasBinding,
                ["hadLocalValue"] = hadLocalValue,
                ["clearedValue"] = clearedValue,
                ["value"] = CreateValueSnapshot(updatedValue, descriptor.BindableProperty.ReturnType, DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties)
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
