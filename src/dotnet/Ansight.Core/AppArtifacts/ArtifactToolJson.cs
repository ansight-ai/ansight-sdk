namespace Ansight.Artifacts;

using System.Text.Json.Nodes;
using Ansight.Tools;

internal static class ArtifactToolJson
{
    internal static JsonObject ToJson(ArtifactProviderDescriptor descriptor, string? error = null)
    {
        return new JsonObject
        {
            ["id"] = descriptor.Id,
            ["name"] = descriptor.Name,
            ["description"] = descriptor.Description,
            ["category"] = descriptor.Category,
            ["tags"] = ToJsonArray(descriptor.Tags),
            ["metadata"] = ToJsonObject(descriptor.Metadata),
            ["error"] = error
        };
    }

    internal static JsonObject ToJson(string providerId, ArtifactDefinition definition)
    {
        return new JsonObject
        {
            ["providerId"] = providerId,
            ["id"] = definition.Id,
            ["name"] = definition.Name,
            ["description"] = definition.Description,
            ["kind"] = definition.Kind,
            ["category"] = definition.Category,
            ["tags"] = ToJsonArray(definition.Tags),
            ["metadata"] = ToJsonObject(definition.Metadata),
            ["content"] = ToJson(definition.Content),
            ["argumentsSchema"] = definition.ArgumentsSchema.ToJson(),
            ["security"] = definition.Security.ToJson()
        };
    }

    internal static JsonObject ToJson(ArtifactContentDescriptor descriptor)
    {
        return new JsonObject
        {
            ["supportedMimeTypes"] = ToJsonArray(descriptor.SupportedMimeTypes),
            ["defaultMimeType"] = descriptor.DefaultMimeType,
            ["suggestedFileName"] = descriptor.SuggestedFileName,
            ["supportsText"] = descriptor.SupportsText,
            ["supportsBinary"] = descriptor.SupportsBinary,
            ["sizeKnownBeforeCreation"] = descriptor.SizeKnownBeforeCreation,
            ["estimatedSizeBytes"] = descriptor.EstimatedSizeBytes
        };
    }

    internal static JsonObject ToJson(ArtifactMetadata metadata)
    {
        return new JsonObject
        {
            ["artifactId"] = metadata.ArtifactId,
            ["providerId"] = metadata.ProviderId,
            ["name"] = metadata.Name,
            ["kind"] = metadata.Kind,
            ["description"] = metadata.Description,
            ["mimeType"] = metadata.MimeType,
            ["fileName"] = metadata.FileName,
            ["sizeBytes"] = metadata.SizeBytes,
            ["createdAtUtc"] = metadata.CreatedAtUtc.ToUniversalTime().ToString("O"),
            ["tags"] = ToJsonArray(metadata.Tags),
            ["metadata"] = ToJsonObject(metadata.Metadata)
        };
    }

    internal static JsonArray ToJsonArray(IEnumerable<string>? values)
    {
        var array = new JsonArray();
        if (values == null)
        {
            return array;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                array.Add(value);
            }
        }

        return array;
    }

    internal static JsonObject ToJsonObject(IReadOnlyDictionary<string, string>? values)
    {
        var json = new JsonObject();
        if (values == null)
        {
            return json;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value.Key))
            {
                json[value.Key] = value.Value;
            }
        }

        return json;
    }
}
