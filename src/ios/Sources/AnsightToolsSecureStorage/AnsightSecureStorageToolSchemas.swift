import AnsightKit
import Foundation

internal enum AnsightSecureStorageToolSchemas {
    static let getValueArguments = object(
        description: "Arguments for retrieving a secure storage value.",
        properties: [
            "key": string("Secure storage key."),
        ],
        required: ["key"]
    )

    static let getValueResult = object(
        description: "Secure storage read payload.",
        properties: [
            "store": string("Resolved secure storage identifier or Keychain service."),
            "key": string("Secure storage key."),
            "exists": boolean("Whether the key exists."),
            "value": string("Decrypted secure storage value.", nullable: true),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "key", "exists", "value", "capturedAtUtc"]
    )

    static let setValueArguments = object(
        description: "Arguments for writing a secure storage value.",
        properties: [
            "key": string("Secure storage key."),
            "value": string("Secure storage value."),
        ],
        required: ["key", "value"]
    )

    static let setValueResult = object(
        description: "Secure storage write payload.",
        properties: [
            "store": string("Resolved secure storage identifier or Keychain service."),
            "key": string("Secure storage key."),
            "updated": boolean("Whether the value was updated."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "key", "updated", "capturedAtUtc"]
    )

    static let removeKeyArguments = object(
        description: "Arguments for deleting a secure storage value.",
        properties: [
            "key": string("Secure storage key."),
        ],
        required: ["key"]
    )

    static let removeKeyResult = object(
        description: "Secure storage delete payload.",
        properties: [
            "store": string("Resolved secure storage identifier or Keychain service."),
            "key": string("Secure storage key."),
            "removed": boolean("Whether the key was removed."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["store", "key", "removed", "capturedAtUtc"]
    )

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

    private static func string(
        _ description: String,
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string("string"), .string("null")]) : .string("string"),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]

        if let format {
            result["format"] = .string(format)
        }

        return .object(result)
    }

    private static func boolean(_ description: String) -> JSONValue {
        .object([
            "type": .string("boolean"),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ])
    }
}
