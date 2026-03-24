namespace Ansight.Tools.SecureStorage;

using Ansight.Tools;

internal static class SecureStorageToolSchemas
{
    internal static ToolSchema GetValueArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving a secure storage value.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["key"] = ToolSchema.String("Secure storage key.")
        },
        required: new[] { "key" });

    internal static ToolSchema GetValueResult { get; } = ToolSchema.Object(
        description: "Secure storage read payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved secure storage identifier or Keychain service."),
            ["key"] = ToolSchema.String("Secure storage key."),
            ["exists"] = ToolSchema.Boolean("Whether the key exists."),
            ["value"] = ToolSchema.String("Decrypted secure storage value.", nullable: true),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "key", "exists", "value", "capturedAtUtc" });

    internal static ToolSchema SetValueArguments { get; } = ToolSchema.Object(
        description: "Arguments for writing a secure storage value.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["key"] = ToolSchema.String("Secure storage key."),
            ["value"] = ToolSchema.String("Secure storage value.")
        },
        required: new[] { "key", "value" });

    internal static ToolSchema SetValueResult { get; } = ToolSchema.Object(
        description: "Secure storage write payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved secure storage identifier or Keychain service."),
            ["key"] = ToolSchema.String("Secure storage key."),
            ["updated"] = ToolSchema.Boolean("Whether the value was updated."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "key", "updated", "capturedAtUtc" });

    internal static ToolSchema RemoveKeyArguments { get; } = ToolSchema.Object(
        description: "Arguments for deleting a secure storage value.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["key"] = ToolSchema.String("Secure storage key.")
        },
        required: new[] { "key" });

    internal static ToolSchema RemoveKeyResult { get; } = ToolSchema.Object(
        description: "Secure storage delete payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved secure storage identifier or Keychain service."),
            ["key"] = ToolSchema.String("Secure storage key."),
            ["removed"] = ToolSchema.Boolean("Whether the key was removed."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "key", "removed", "capturedAtUtc" });
}
