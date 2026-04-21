namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

#if ANDROID || IOS || MACCATALYST
using Microsoft.Maui.Controls;
#endif

public sealed class GetMauiBindingsTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetBindings;

    public string Name => "Get MAUI Bindings";

    public string Description => "Enumerates active MAUI binding expressions for a node in the current visual tree.";

    public string Keywords => "maui bindings binding path mode source converter diagnostics";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetBindingsArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindingsResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetBindings;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var propertyName = GetString(arguments, "propertyName");
            var includeUnbound = GetBoolean(arguments, "includeUnbound", defaultValue: false);
            var includeValues = GetBoolean(arguments, "includeValues", defaultValue: false);
            var maxProperties = GetInt(arguments, "maxProperties", DefaultMaxProperties, minimum: 1, maximum: MaximumMaxProperties);

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            if (resolution.Element is not BindableObject bindable)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' is not a BindableObject.", errorCode: "maui_node_not_bindable");
            }

            var descriptors = GetBindablePropertyDescriptors(bindable.GetType())
                .Where(descriptor => string.IsNullOrWhiteSpace(propertyName) || IsBindablePropertyMatch(descriptor, propertyName))
                .ToArray();

            var bindings = new JsonArray();
            var matchedPropertyCount = 0;
            var truncated = false;
            foreach (var descriptor in descriptors)
            {
                var binding = CreateBindingInfo(bindable, descriptor.BindableProperty);
                if (binding == null && !includeUnbound)
                {
                    continue;
                }

                matchedPropertyCount++;
                if (bindings.Count >= maxProperties)
                {
                    truncated = true;
                    continue;
                }

                var bindingJson = new JsonObject
                {
                    ["property"] = CreateBindablePropertyMetadata(bindable, descriptor),
                    ["binding"] = binding
                };

                if (includeValues)
                {
                    var value = bindable.GetValue(descriptor.BindableProperty);
                    bindingJson["value"] = CreateValueSnapshot(value, descriptor.BindableProperty.ReturnType, DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties);
                }

                bindings.Add(bindingJson);
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["bindingContextType"] = resolution.Element.BindingContext == null ? null : CreateTypeMetadata(resolution.Element.BindingContext.GetType()),
                ["inspectedPropertyCount"] = descriptors.Length,
                ["matchedPropertyCount"] = matchedPropertyCount,
                ["returnedCount"] = bindings.Count,
                ["truncated"] = truncated,
                ["bindings"] = bindings
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
