namespace Ansight.Tools.Preferences;

using Ansight.Tools;

internal static class PreferencesToolSchemas
{
    private static readonly string[] ResultValueTypeValues =
    {
        "string",
        "boolean",
        "integer",
        "number",
        "string_array",
        "unsupported"
    };

    private static readonly string[] WritableValueTypeValues =
    {
        "string",
        "boolean",
        "integer",
        "number",
        "string_array"
    };

    internal static ToolSchema ListKeysArguments { get; } = ToolSchema.Object(
        description: "Arguments for listing keys from a shared preferences or user defaults store.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Optional shared preferences store or user defaults suite name.", nullable: true),
            ["prefix"] = ToolSchema.String("Optional key prefix filter.", nullable: true),
            ["maxResults"] = ToolSchema.Integer("Maximum number of keys to return.")
        });

    internal static ToolSchema ListKeysResult { get; } = ToolSchema.Object(
        description: "Preferences key listing payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved preferences store."),
            ["keys"] = ToolSchema.Array(ToolSchema.String("Preference key."), "Filtered preference keys."),
            ["truncated"] = ToolSchema.Boolean("Whether additional keys were omitted."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "keys", "truncated", "capturedAtUtc" });

    internal static ToolSchema GetValueArguments { get; } = ToolSchema.Object(
        description: "Arguments for retrieving a preference value.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["key"] = ToolSchema.String("Preference key."),
            ["store"] = ToolSchema.String("Optional shared preferences store or user defaults suite name.", nullable: true)
        },
        required: new[] { "key" });

    internal static ToolSchema GetValueResult { get; } = ToolSchema.Object(
        description: "Preference value payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved preferences store."),
            ["key"] = ToolSchema.String("Preference key."),
            ["exists"] = ToolSchema.Boolean("Whether the key exists."),
            ["value"] = ToolSchema.String("Stringified preference value. Arrays are represented as JSON text.", nullable: true),
            ["valueType"] = ToolSchema.String("Normalized preference value type.", enumValues: ResultValueTypeValues, nullable: true),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "key", "exists", "value", "valueType", "capturedAtUtc" });

    internal static ToolSchema SetValueArguments { get; } = ToolSchema.Object(
        description: "Arguments for writing a preference value.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["key"] = ToolSchema.String("Preference key."),
            ["value"] = ToolSchema.String("Stringified preference value. Arrays must be provided as JSON text."),
            ["valueType"] = ToolSchema.String("Preference value type.", enumValues: WritableValueTypeValues),
            ["store"] = ToolSchema.String("Optional shared preferences store or user defaults suite name.", nullable: true)
        },
        required: new[] { "key", "value", "valueType" });

    internal static ToolSchema SetValueResult { get; } = ToolSchema.Object(
        description: "Preference write payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved preferences store."),
            ["key"] = ToolSchema.String("Preference key."),
            ["valueType"] = ToolSchema.String("Stored preference value type.", enumValues: WritableValueTypeValues),
            ["updated"] = ToolSchema.Boolean("Whether the value was updated."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "key", "valueType", "updated", "capturedAtUtc" });

    internal static ToolSchema RemoveKeyArguments { get; } = ToolSchema.Object(
        description: "Arguments for deleting a preference key.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["key"] = ToolSchema.String("Preference key."),
            ["store"] = ToolSchema.String("Optional shared preferences store or user defaults suite name.", nullable: true)
        },
        required: new[] { "key" });

    internal static ToolSchema RemoveKeyResult { get; } = ToolSchema.Object(
        description: "Preference delete payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["store"] = ToolSchema.String("Resolved preferences store."),
            ["key"] = ToolSchema.String("Preference key."),
            ["removed"] = ToolSchema.Boolean("Whether the key was removed."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "store", "key", "removed", "capturedAtUtc" });
}
