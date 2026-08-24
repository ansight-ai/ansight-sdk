namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class SetBindingContextPropertyTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => MauiToolIds.SetBindingContextProperty;

    public string Name => "Set Binding Context Property";

    public string Description => "Writes a public property on a MAUI element binding-context object.";

    public string Keywords => "maui binding context property set mutate viewmodel";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.SetBindingContextPropertyArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindingContextMutationResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var propertyName = GetRequiredString(arguments, "propertyName");
            var valueJson = GetRequiredString(arguments, "valueJson");

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            var bindingContext = resolution.Element.BindingContext;
            if (bindingContext == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' does not have a binding context.", errorCode: "maui_binding_context_unavailable");
            }

            if (!TrySetPublicPropertyFromJson(bindingContext, propertyName, valueJson, out var updatedValue, out var error))
            {
                return ToolResult.Failure(error ?? "The binding-context property could not be set.", errorCode: "maui_binding_context_property_set_failed");
            }

            var property = ResolvePublicInstanceProperty(bindingContext.GetType(), propertyName);
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["bindingContextType"] = CreateTypeMetadata(bindingContext.GetType()),
                ["propertyName"] = property?.Name ?? propertyName,
                ["updated"] = true,
                ["value"] = CreateValueSnapshot(updatedValue, property?.PropertyType ?? updatedValue?.GetType(), DefaultObjectDepth, DefaultMaxItems, DefaultMaxProperties)
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
