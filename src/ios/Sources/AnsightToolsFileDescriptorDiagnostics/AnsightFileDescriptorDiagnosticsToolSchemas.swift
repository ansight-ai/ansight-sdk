import AnsightCore
import Foundation

internal enum AnsightFileDescriptorDiagnosticsToolSchemas {
    static let listOpenArguments = object(
        description: "Arguments for listing open file descriptors.",
        properties: [
            "kind": string("Optional descriptor kind filter.", enumValues: descriptorKinds, nullable: true),
            "targetContains": string("Optional case-insensitive target substring filter.", nullable: true),
            "maxEntries": integer("Maximum descriptors to return after filtering."),
        ]
    )

    static let listOpenResult = object(
        description: "Open file descriptor listing.",
        properties: snapshotProperties.merging([
            "count": integer("Total number of open descriptors found by the scan."),
            "matchedCount": integer("Number of descriptors matching the filters."),
            "returnedCount": integer("Number of descriptor records returned."),
            "descriptors": array(descriptor, "Open descriptor records."),
            "truncated": boolean("Whether matching records were omitted by maxEntries."),
        ]) { _, new in new },
        required: snapshotRequired + ["count", "matchedCount", "returnedCount", "descriptors", "truncated"]
    )

    static let countOpenArguments = object(
        description: "No arguments are required for counting open file descriptors.",
        properties: [:]
    )

    static let countOpenResult = object(
        description: "Open file descriptor count.",
        properties: [
            "count": integer("Number of open descriptors found by the scan."),
        ],
        required: ["count"]
    )

    static let inspectArguments = object(
        description: "Arguments for inspecting one open file descriptor.",
        properties: [
            "descriptor": integer("Non-negative file descriptor number."),
        ],
        required: ["descriptor"]
    )

    static let inspectResult = object(
        description: "One open file descriptor record.",
        properties: [
            "descriptor": descriptor,
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["descriptor", "capturedAtUtc"]
    )

    static let getUsageArguments = object(
        description: "No arguments are required for reading file descriptor usage.",
        properties: [:]
    )

    static let getUsageResult = object(
        description: "File descriptor limits and current utilization.",
        properties: snapshotProperties.merging([
            "openCount": integer("Number of open descriptors found by the scan."),
            "softLimit": integer("Current process soft descriptor limit.", nullable: true),
            "hardLimit": integer("Current process hard descriptor limit, or null when unlimited.", nullable: true),
            "hardLimitUnlimited": boolean("Whether the process hard limit is unlimited."),
            "availableBeforeSoftLimit": integer("Remaining descriptors before the soft limit, or null when the count is incomplete.", nullable: true),
            "utilizationPercent": number("Percentage of the soft limit currently in use, or null when unavailable.", nullable: true),
        ]) { _, new in new },
        required: snapshotRequired + [
            "openCount",
            "softLimit",
            "hardLimit",
            "hardLimitUnlimited",
            "availableBeforeSoftLimit",
            "utilizationPercent",
        ]
    )

    private static let descriptorKinds = AnsightFileDescriptorKind.allCases.map(\.rawValue)
    private static let snapshotRequired = ["scanComplete", "scannedDescriptorLimit", "capturedAtUtc"]
    private static let snapshotProperties: [String: JSONValue] = [
        "scanComplete": boolean("Whether the collector scanned the complete soft descriptor range."),
        "scannedDescriptorLimit": integer("Exclusive upper descriptor bound used by the collector."),
        "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
    ]

    private static let descriptor = objectJSON(
        description: "Open file descriptor metadata.",
        properties: [
            "descriptor": integer("File descriptor number."),
            "kind": string("Descriptor resource kind.", enumValues: descriptorKinds),
            "target": string("Resolved descriptor target when enabled and available.", nullable: true),
            "accessMode": string("Descriptor access mode when available.", enumValues: ["read_only", "write_only", "read_write", "unknown"], nullable: true),
            "closeOnExec": boolean("Whether close-on-exec is enabled.", nullable: true),
            "descriptorFlags": integer("Raw descriptor flags when available.", nullable: true),
            "statusFlags": integer("Raw open status flags when available.", nullable: true),
            "positionBytes": integer("Current descriptor position when seekable.", nullable: true),
            "inode": integer("Backing inode when available.", nullable: true),
        ],
        required: [
            "descriptor",
            "kind",
            "target",
            "accessMode",
            "closeOnExec",
            "descriptorFlags",
            "statusFlags",
            "positionBytes",
            "inode",
        ]
    )

    private static func object(
        description: String,
        properties: [String: JSONValue],
        required: [String] = []
    ) -> AnsightToolSchema {
        AnsightToolSchema(json: objectJSON(description: description, properties: properties, required: required))
    }

    private static func objectJSON(
        description: String,
        properties: [String: JSONValue],
        required: [String] = []
    ) -> JSONValue {
        var value: [String: JSONValue] = [
            "type": .string("object"),
            "additionalProperties": .bool(false),
            "description": .string(description),
            "properties": .object(properties),
        ]
        if !required.isEmpty {
            value["required"] = .array(required.map(JSONValue.string))
        }
        return .object(value)
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
        primitive(type: "string", description: description, enumValues: enumValues, nullable: nullable, format: format)
    }

    private static func integer(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "integer", description: description, nullable: nullable)
    }

    private static func number(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "number", description: description, nullable: nullable)
    }

    private static func boolean(_ description: String, nullable: Bool = false) -> JSONValue {
        primitive(type: "boolean", description: description, nullable: nullable)
    }

    private static func primitive(
        type: String,
        description: String,
        enumValues: [String] = [],
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        var value: [String: JSONValue] = [
            "type": nullable ? .array([.string(type), .string("null")]) : .string(type),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]
        if !enumValues.isEmpty {
            value["enum"] = .array(enumValues.map(JSONValue.string))
        }
        if let format {
            value["format"] = .string(format)
        }
        return .object(value)
    }
}
