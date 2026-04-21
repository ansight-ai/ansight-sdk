namespace Ansight.Tools.Maui;

using System.Reflection;
using System.Text.Json.Nodes;
using static MauiToolHelpers;

public sealed class GetMauiHandlerDiagnosticsTool : ITool
{
    public string Category => "maui";

    public ToolScope Scope => ToolScope.Read;

    public string Id => MauiToolIds.GetHandlerDiagnostics;

    public string Name => "Get MAUI Handler Diagnostics";

    public string Description => "Returns MAUI handler and platform-view metadata for a node in the current visual tree.";

    public string Keywords => "maui handler platform view native diagnostics";

    public ToolSchema ArgumentsSchema => MauiToolSchemas.GetHandlerDiagnosticsArguments;

    public ToolSchema ResultSchema => MauiToolSchemas.DiagnosticsResult;

    public ToolSecurity Security => MauiToolSecurityProfiles.GetHandlerDiagnostics;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

#if ANDROID || IOS || MACCATALYST
        return RunOnMainThreadAsync(() =>
        {
            var nodeId = GetRequiredString(arguments, "nodeId");
            var includePlatformViewProperties = GetBoolean(arguments, "includePlatformViewProperties", defaultValue: false);
            var maxProperties = GetInt(arguments, "maxProperties", DefaultMaxProperties, minimum: 1, maximum: MaximumMaxProperties);

            var resolution = ResolveElement(nodeId);
            if (resolution == null)
            {
                return ToolResult.Failure($"The MAUI node '{nodeId}' was not found.", errorCode: "maui_node_not_found");
            }

            var element = resolution.Element;
            TryReadPublicProperty(element, "Handler", out var handler, out var handlerDeclaredType);

            var handlerJson = new JsonObject
            {
                ["isNull"] = handler == null,
                ["declaredType"] = handlerDeclaredType == null ? null : CreateTypeMetadata(handlerDeclaredType),
                ["runtimeType"] = handler == null ? null : CreateTypeMetadata(handler.GetType())
            };

            object? platformView = null;
            Type? platformViewType = null;
            if (handler != null)
            {
                if (TryReadPublicProperty(handler, "VirtualView", out var virtualView, out var virtualViewType))
                {
                    handlerJson["virtualView"] = CreateValueMetadataSnapshot(virtualView, virtualViewType);
                }

                if (TryReadPublicProperty(handler, "MauiContext", out var mauiContext, out var mauiContextType))
                {
                    handlerJson["mauiContext"] = CreateValueMetadataSnapshot(mauiContext, mauiContextType);
                }

                if (TryReadPublicProperty(handler, "PlatformView", out platformView, out platformViewType) ||
                    TryReadPublicProperty(handler, "NativeView", out platformView, out platformViewType))
                {
                    handlerJson["platformView"] = CreateValueMetadataSnapshot(platformView, platformViewType);
                }
            }

            if (includePlatformViewProperties && platformView != null)
            {
                var properties = new JsonObject();
                var propertyCount = 0;
                foreach (var property in platformView.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (propertyCount >= maxProperties)
                    {
                        handlerJson["platformViewPropertiesTruncated"] = true;
                        break;
                    }

                    if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    object? propertyValue;
                    try
                    {
                        propertyValue = property.GetValue(platformView);
                    }
                    catch
                    {
                        continue;
                    }

                    properties[property.Name] = CreateValueSnapshot(propertyValue, property.PropertyType, depthRemaining: 0, DefaultMaxItems, DefaultMaxProperties);
                    propertyCount++;
                }

                handlerJson["platformViewProperties"] = properties;
            }

            var payload = new JsonObject
            {
                ["platform"] = CurrentPlatform,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["node"] = CreateElementReference(element),
                ["handler"] = handlerJson
            };

            return ToolResult.Success(payload);
        });
#else
        return Task.FromResult(CreateUnsupportedResult());
#endif
    }
}
