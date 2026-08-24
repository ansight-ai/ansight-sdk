namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// One versioned protocol capability and its optional feature identifiers.
/// </summary>
public sealed record ToolCapabilityDefinition(
    int Version,
    IReadOnlyList<string> Features)
{
    internal JsonObject ToJson()
        => new()
        {
            ["version"] = Version,
            ["features"] = new JsonArray(
                Features
                    .OrderBy(feature => feature, StringComparer.Ordinal)
                    .Select(feature => (JsonNode?)JsonValue.Create(feature))
                    .ToArray())
        };
}

/// <summary>
/// Versioned capability manifest published with a tool catalog.
/// </summary>
public sealed record ToolCapabilityManifest(
    string Schema,
    string Revision,
    IReadOnlyDictionary<string, ToolCapabilityDefinition> Capabilities)
{
    /// <summary>
    /// Current capability manifest schema identifier.
    /// </summary>
    public const string CurrentSchema = "ansight.capabilities.v1";

    internal JsonObject ToJson()
    {
        var capabilities = new JsonObject();
        foreach (var capability in Capabilities.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            capabilities[capability.Key] = capability.Value.ToJson();
        }

        return new JsonObject
        {
            ["schema"] = Schema,
            ["revision"] = Revision,
            ["capabilities"] = capabilities
        };
    }
}
