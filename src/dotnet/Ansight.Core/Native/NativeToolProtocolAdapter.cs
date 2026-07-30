using Ansight.Tools;
using System.Text.Json.Nodes;

namespace Ansight.Native;

internal static class NativeToolProtocolAdapter
{
    internal static string CreateSessionCatalogJson(ToolProtocolBridge toolBridge)
    {
        ArgumentNullException.ThrowIfNull(toolBridge);
        var visibleTools = toolBridge.Guard.DiscoveryEnabled
            ? toolBridge.GetVisibleTools()
            : Array.Empty<ToolDefinition>();
        var ids = new JsonArray(visibleTools
            .Select(tool => (JsonNode?)JsonValue.Create(tool.Id))
            .ToArray());
        var categories = new JsonArray(visibleTools
            .Select(tool => tool.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(category => (JsonNode?)JsonValue.Create(category))
            .ToArray());
        return new JsonObject
        {
            ["toolIds"] = ids,
            ["toolCategories"] = categories
        }.ToJsonString();
    }

    internal static string? Handle(ToolProtocolBridge toolBridge, string? requestJson)
    {
        ArgumentNullException.ThrowIfNull(toolBridge);
        var error = "The request was empty.";
        if (string.IsNullOrWhiteSpace(requestJson) ||
            !toolBridge.TryParseEnvelope(requestJson, out var envelope, out error) ||
            envelope is null)
        {
            Logger.Warning($"The native runtime supplied an invalid tool protocol request: {error}");
            return null;
        }

        var response = toolBridge.HandleAsync(envelope, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        return toolBridge.SerializeEnvelope(response);
    }

    internal static void ResponseSent(ToolProtocolBridge toolBridge, string? requestJson)
    {
        ArgumentNullException.ThrowIfNull(toolBridge);
        if (string.IsNullOrWhiteSpace(requestJson) ||
            !toolBridge.TryParseEnvelope(requestJson, out var envelope, out _) ||
            envelope is null ||
            !string.Equals(envelope.Type, ToolProtocolBridge.CallType, StringComparison.Ordinal) ||
            !Runtime.IsInitialized)
        {
            return;
        }

        Runtime.MutableInstance.BinaryTransferHub.TryStartQueuedTransfer(envelope.Id);
    }
}
