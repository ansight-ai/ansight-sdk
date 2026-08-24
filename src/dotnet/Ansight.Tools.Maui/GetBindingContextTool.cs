namespace Ansight.Tools.Maui;

using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class GetBindingContextTool : ITool
{
    public string Category => "maui";

    public ToolPolicy Policy => ToolPolicy.Critical;

    public string Id => MauiToolIds.GetBindingContext;

    public string Name => "Get Binding Context";

    public string Description => "Returns binding-context metadata for a node in the current .NET MAUI visual tree.";

    public string Keywords => "maui binding context viewmodel object type data";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetBindingContextArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.BindingContextResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var includeProperties = GetBoolean(arguments, "includeProperties", defaultValue: false);
            var maxDepth = GetInt(arguments, "maxDepth", DefaultObjectDepth, minimum: 0, maximum: MaximumObjectDepth);
            var maxProperties = GetInt(arguments, "maxProperties", DefaultMaxProperties, minimum: 1, maximum: MaximumMaxProperties);

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            var bindingContext = resolution.Element.BindingContext;
            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(resolution.Element),
                ["hasBindingContext"] = bindingContext != null,
                ["bindingContext"] = includeProperties
                    ? CreateValueSnapshot(bindingContext, bindingContext?.GetType(), maxDepth, DefaultMaxItems, maxProperties)
                    : CreateValueMetadataSnapshot(bindingContext, bindingContext?.GetType())
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
