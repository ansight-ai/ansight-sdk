import AnsightKit
import Foundation

internal enum AnsightPreferencesToolSchemas {
    static let listKeysArguments = object(
        description: "Arguments for listing keys from a shared preferences or user defaults store.",
        properties: [
            "store": string("Optional shared preferences store or user defaults suite name.", nullable: true),
            "prefix": string("Optional key prefix filter.", nullable: true),
            "maxResults": integer("Maximum number of keys to return."),
        ]
    )

    static let listKeysResult = object(
        description: "Preferences key listing payload.",
        properties: [
            "store": string("Resolved preferences store."),
            "keys": array(string("Preference key."), "Filtered preference keys."),
            "truncated": boolean("Whether additional keys were omitted."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "keys", "truncated", "capturedAtUtc"]
    )

    static let getValueArguments = object(
        description: "Arguments for retrieving a preference value.",
        properties: [
            "key": string("Preference key."),
            "store": string("Optional shared preferences store or user defaults suite name.", nullable: true),
        ],
        required: ["key"]
    )

    static let getValueResult = object(
        description: "Preference value payload.",
        properties: [
            "store": string("Resolved preferences store."),
            "key": string("Preference key."),
            "exists": boolean("Whether the key exists."),
            "value": string("Stringified preference value. Arrays are represented as JSON text.", nullable: true),
            "valueType": string("Normalized preference value type.", enumValues: resultValueTypes, nullable: true),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "key", "exists", "value", "valueType", "capturedAtUtc"]
    )

    static let setValueArguments = object(
        description: "Arguments for writing a preference value.",
        properties: [
            "key": string("Preference key."),
            "value": string("Stringified preference value. Arrays must be provided as JSON text."),
            "valueType": string("Preference value type.", enumValues: writableValueTypes),
            "store": string("Optional shared preferences store or user defaults suite name.", nullable: true),
        ],
        required: ["key", "value", "valueType"]
    )

    static let setValueResult = object(
        description: "Preference write payload.",
        properties: [
            "store": string("Resolved preferences store."),
            "key": string("Preference key."),
            "valueType": string("Stored preference value type.", enumValues: writableValueTypes),
            "updated": boolean("Whether the value was updated."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "key", "valueType", "updated", "capturedAtUtc"]
    )

    static let removeKeyArguments = object(
        description: "Arguments for deleting a preference key.",
        properties: [
            "key": string("Preference key."),
            "store": string("Optional shared preferences store or user defaults suite name.", nullable: true),
        ],
        required: ["key"]
    )

    static let removeKeyResult = object(
        description: "Preference delete payload.",
        properties: [
            "store": string("Resolved preferences store."),
            "key": string("Preference key."),
            "removed": boolean("Whether the key was removed."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "key", "removed", "capturedAtUtc"]
    )

    private static let resultValueTypes = [
        "string",
        "boolean",
        "integer",
        "number",
        "string_array",
        "unsupported",
    ]

    private static let writableValueTypes = [
        "string",
        "boolean",
        "integer",
        "number",
        "string_array",
    ]

    private static func object(
        description: String,
        properties: [String: JSONValue],
        required: [String] = []
    ) -> AnsightToolSchema {
        var result: [String: JSONValue] = [
            "type": .string("object"),
            "additionalProperties": .bool(false),
            "description": .string(description),
            "properties": .object(properties),
        ]

        if !required.isEmpty {
            result["required"] = .array(required.map(JSONValue.string))
        }

        return AnsightToolSchema(json: .object(result))
    }

    private static func array(_ items: JSONValue, _ description: String) -> JSONValue {
        .object([
            "type": .string("array"),
            "additionalProperties": .bool(false),
            "description": .string(description),
            "items": items,
        ])
    }

    private static func string(
        _ description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        primitive(
            type: "string",
            description: description,
            enumValues: enumValues,
            nullable: nullable,
            format: format
        )
    }

    private static func integer(_ description: String) -> JSONValue {
        primitive(type: "integer", description: description)
    }

    private static func boolean(_ description: String) -> JSONValue {
        primitive(type: "boolean", description: description)
    }

    private static func primitive(
        type: String,
        description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string(type), .string("null")]) : .string(type),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]

        if !enumValues.isEmpty {
            result["enum"] = .array(enumValues.map(JSONValue.string))
        }

        if let format {
            result["format"] = .string(format)
        }

        return .object(result)
    }
}
