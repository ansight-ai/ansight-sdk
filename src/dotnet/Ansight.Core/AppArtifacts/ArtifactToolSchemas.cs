namespace Ansight.Artifacts;

using Ansight.Tools;

internal static class ArtifactToolSchemas
{
    private static readonly ToolSchema StringMapSchema = ToolSchema.Object(
        description: "String key/value metadata.",
        additionalProperties: true);

    private static readonly ToolSchema ProviderSchema = ToolSchema.Object(
        description: "Registered artifact provider.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["id"] = ToolSchema.String("Provider id."),
            ["name"] = ToolSchema.String("Provider name."),
            ["description"] = ToolSchema.String("Provider description."),
            ["category"] = ToolSchema.String("Provider category."),
            ["tags"] = ToolSchema.Array(ToolSchema.String("Provider tag."), "Provider tags."),
            ["metadata"] = StringMapSchema,
            ["error"] = ToolSchema.String("Provider query error, when the provider could not be queried.", nullable: true)
        },
        required: new[] { "id", "name", "description", "category", "tags", "metadata", "error" });

    private static readonly ToolSchema ContentSchema = ToolSchema.Object(
        description: "Artifact content descriptor.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["supportedMimeTypes"] = ToolSchema.Array(ToolSchema.String("Supported MIME type."), "Supported MIME types."),
            ["defaultMimeType"] = ToolSchema.String("Default MIME type.", nullable: true),
            ["suggestedFileName"] = ToolSchema.String("Suggested artifact file name.", nullable: true),
            ["supportsText"] = ToolSchema.Boolean("Whether the provider can produce text content."),
            ["supportsBinary"] = ToolSchema.Boolean("Whether the provider can produce binary content."),
            ["sizeKnownBeforeCreation"] = ToolSchema.Boolean("Whether size is known before creation."),
            ["estimatedSizeBytes"] = ToolSchema.Integer("Estimated artifact size in bytes.", nullable: true)
        },
        required: new[] { "supportedMimeTypes", "defaultMimeType", "suggestedFileName", "supportsText", "supportsBinary", "sizeKnownBeforeCreation", "estimatedSizeBytes" });

    private static readonly ToolSchema ArtifactDefinitionSchema = ToolSchema.Object(
        description: "Available artifact definition.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["providerId"] = ToolSchema.String("Provider id."),
            ["id"] = ToolSchema.String("Artifact id."),
            ["name"] = ToolSchema.String("Artifact name."),
            ["description"] = ToolSchema.String("Artifact description."),
            ["kind"] = ToolSchema.String("Artifact kind."),
            ["category"] = ToolSchema.String("Artifact category."),
            ["tags"] = ToolSchema.Array(ToolSchema.String("Artifact tag."), "Artifact tags."),
            ["metadata"] = StringMapSchema,
            ["content"] = ContentSchema,
            ["argumentsSchema"] = ToolSchema.Object("Provider-specific request argument schema.", additionalProperties: true),
            ["policy"] = ToolSchema.String(
                "Policy required to request the artifact.",
                enumValues: new[] { "read", "write", "critical" })
        },
        required: new[] { "providerId", "id", "name", "description", "kind", "category", "tags", "metadata", "content", "argumentsSchema", "policy" });

    private static readonly ToolSchema ArtifactMetadataSchema = ToolSchema.Object(
        description: "Created artifact metadata.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["artifactId"] = ToolSchema.String("Artifact id."),
            ["providerId"] = ToolSchema.String("Provider id."),
            ["name"] = ToolSchema.String("Artifact name."),
            ["kind"] = ToolSchema.String("Artifact kind."),
            ["description"] = ToolSchema.String("Artifact description.", nullable: true),
            ["mimeType"] = ToolSchema.String("MIME type."),
            ["fileName"] = ToolSchema.String("Suggested artifact file name."),
            ["sizeBytes"] = ToolSchema.Integer("Artifact size in bytes.", nullable: true),
            ["createdAtUtc"] = ToolSchema.String("UTC timestamp when the artifact was created.", format: "date-time"),
            ["tags"] = ToolSchema.Array(ToolSchema.String("Artifact tag."), "Artifact tags."),
            ["metadata"] = StringMapSchema
        },
        required: new[] { "artifactId", "providerId", "name", "kind", "description", "mimeType", "fileName", "sizeBytes", "createdAtUtc", "tags", "metadata" });

    internal static ToolSchema QueryArguments { get; } = ToolSchema.Object(
        description: "Arguments for querying app-provided artifacts.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["providerId"] = ToolSchema.String("Optional provider id to query.", nullable: true),
            ["category"] = ToolSchema.String("Optional artifact category filter.", nullable: true),
            ["kind"] = ToolSchema.String("Optional artifact kind filter.", nullable: true),
            ["tag"] = ToolSchema.String("Optional artifact tag filter.", nullable: true)
        });

    internal static ToolSchema QueryResult { get; } = ToolSchema.Object(
        description: "Artifact catalog payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["providers"] = ToolSchema.Array(ProviderSchema, "Registered artifact providers."),
            ["artifacts"] = ToolSchema.Array(ArtifactDefinitionSchema, "Available artifact definitions."),
            ["providerCount"] = ToolSchema.Integer("Number of providers returned."),
            ["artifactCount"] = ToolSchema.Integer("Number of artifact definitions returned."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for the query.", format: "date-time")
        },
        required: new[] { "providers", "artifacts", "providerCount", "artifactCount", "capturedAtUtc" });

    internal static ToolSchema RequestArguments { get; } = ToolSchema.Object(
        description: "Arguments for requesting an app-provided artifact snapshot.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["providerId"] = ToolSchema.String("Provider id."),
            ["artifactId"] = ToolSchema.String("Artifact id."),
            ["downloadId"] = ToolSchema.String("Optional caller-supplied correlation id for mapping the transfer to a host artifact file.", nullable: true),
            ["chunkBytes"] = ToolSchema.Integer("Maximum bytes to include in each binary WebSocket frame."),
            ["arguments"] = ToolSchema.Object("Provider-specific artifact request arguments.", additionalProperties: true, nullable: true)
        },
        required: new[] { "providerId", "artifactId" });

    internal static ToolSchema RequestResult { get; } = ToolSchema.Object(
        description: "Requested artifact transfer payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["artifact"] = ArtifactMetadataSchema,
            ["downloadId"] = ToolSchema.String("Caller correlation id for the host-side artifact file."),
            ["transferId"] = ToolSchema.String("Transfer id carried in binary frame headers."),
            ["deliveryMode"] = ToolSchema.String("How artifact bytes are delivered.", enumValues: new[] { "websocket_binary" }),
            ["wireProtocol"] = ToolSchema.String("Binary wire protocol identifier."),
            ["status"] = ToolSchema.String("Initial transfer state.", enumValues: new[] { "queued" }),
            ["chunkBytes"] = ToolSchema.Integer("Maximum bytes per binary frame."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for the request.", format: "date-time")
        },
        required: new[] { "artifact", "downloadId", "transferId", "deliveryMode", "wireProtocol", "status", "chunkBytes", "capturedAtUtc" });
}
