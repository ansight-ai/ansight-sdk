import AnsightCore
import Foundation

internal enum AnsightReflectionToolSchemas {
    static let listRootsArguments = object(description: "Arguments for listing registered reflection roots.")

    static let listRootsResult = object(
        description: "Registered reflection roots.",
        properties: [
            "roots": array(rootDescriptor, "Registered roots."),
            "count": integer("Number of roots."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["roots", "count", "capturedAtUtc"]
    )

    static let inspectObjectArguments = object(
        description: "Arguments for inspecting a registered live object.",
        properties: [
            "root": string("Registered root identifier."),
            "path": string("Optional nested member or collection path.", nullable: true),
            "maxDepth": integer("Maximum recursive expansion depth."),
            "maxItemsPerCollection": integer("Maximum array, set, or dictionary items to expand."),
        ],
        required: ["root"]
    )

    static let inspectObjectResult = object(
        description: "Live object inspection payload.",
        properties: [
            "root": string("Registered root identifier."),
            "path": string("Resolved relative path.", nullable: true),
            "snapshot": genericObject,
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["root", "snapshot", "capturedAtUtc"]
    )

    static let describeTypeArguments = object(
        description: "Arguments for describing a runtime type.",
        properties: [
            "typeName": string("Runtime type name.", nullable: true),
            "type": string("Runtime type name.", nullable: true),
            "root": string("Registered root identifier.", nullable: true),
            "path": string("Optional nested member or collection path.", nullable: true),
        ]
    )

    static let describeTypeResult = object(
        description: "Type metadata payload.",
        properties: [
            "typeName": string("Resolved runtime type name."),
            "assemblyName": string("Resolved module or bundle name."),
            "namespace": string("Type namespace.", nullable: true),
            "kind": string("Type category."),
            "baseType": string("Base type name.", nullable: true),
            "interfaces": array(string("Interface or protocol type name."), "Implemented interfaces."),
            "genericArity": integer("Generic type arity."),
            "memberVisibility": string("Visibility rule applied."),
            "members": array(genericObject, "Visible fields and properties."),
            "methods": array(genericObject, "Visible methods."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["typeName", "assemblyName", "kind", "interfaces", "genericArity", "memberVisibility", "members", "methods", "capturedAtUtc"]
    )

    static let setMemberValueArguments = object(
        description: "Arguments for setting a writable live member.",
        properties: [
            "root": string("Registered root identifier."),
            "path": string("Relative member path to write."),
            "valueJson": string("JSON-encoded replacement value."),
            "member": string("Legacy member name alias.", nullable: true),
            "name": string("Legacy member name alias.", nullable: true),
            "value": string("Legacy string replacement value alias.", nullable: true),
        ],
        required: ["root"]
    )

    static let setMemberValueResult = object(
        description: "Member write payload.",
        properties: [
            "root": string("Registered root identifier."),
            "path": string("Written member path."),
            "updated": boolean("Whether the value was updated."),
            "snapshot": genericObject,
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["root", "path", "updated", "snapshot", "capturedAtUtc"]
    )

    static let invokeMethodArguments = object(
        description: "Arguments for invoking an instance method reachable from a registered root.",
        properties: [
            "root": string("Registered root identifier."),
            "targetPath": string("Optional relative path to the invocation target object.", nullable: true),
            "method": string("Method name."),
            "name": string("Legacy method name alias.", nullable: true),
            "argumentsJson": string("Optional JSON array of method arguments.", nullable: true),
        ],
        required: ["root"]
    )

    static let invokeMethodResult = object(
        description: "Method invocation payload.",
        properties: [
            "root": string("Registered root identifier."),
            "targetPath": string("Invocation target path.", nullable: true),
            "signature": string("Canonical invoked method signature."),
            "invoked": boolean("Whether the method was invoked."),
            "returnSnapshot": genericObject,
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["root", "signature", "invoked", "returnSnapshot", "capturedAtUtc"]
    )

    private static let genericObject: JSONValue = .object([
        "type": .string("object"),
        "additionalProperties": .bool(true),
        "description": .string("Arbitrary object with implementation-specific fields."),
    ])

    private static let rootMetadataDescriptor: JSONValue = .object([
        "type": .string("object"),
        "additionalProperties": .bool(false),
        "description": .string("Registered root metadata."),
        "properties": .object([
            "displayName": string("Human-readable root name."),
            "description": string("Optional root description.", nullable: true),
            "hints": array(string("Root hint."), "Optional metadata hints."),
        ]),
        "required": .array([.string("displayName")]),
    ])

    private static let hostRuntimeDescriptor: JSONValue = .object([
        "type": .string("object"),
        "additionalProperties": .bool(true),
        "description": .string("Runtime that owns and resolves the reflection root."),
        "properties": .object([
            "kind": string("Stable runtime host kind, such as dotnet, jvm, swift, or javascript."),
            "displayName": string("Human-readable runtime host name."),
            "platform": string("Platform or SDK surface that exposes the runtime.", nullable: true),
            "engine": string("Runtime engine name when known.", nullable: true),
            "bridge": string("Optional bridge used to reach the runtime.", nullable: true),
        ]),
        "required": .array([.string("kind"), .string("displayName")]),
    ])

    private static let rootDescriptor: JSONValue = .object([
        "type": .string("object"),
        "additionalProperties": .bool(true),
        "description": .string("Registered reflection root descriptor."),
        "properties": .object([
            "id": string("Stable root identifier."),
            "metadata": rootMetadataDescriptor,
            "hostRuntime": hostRuntimeDescriptor,
            "referenceType": string("Reference type for the root."),
            "available": boolean("Whether the root currently resolves to a live object."),
            "runtimeType": string("Resolved runtime type name when available.", nullable: true),
            "type": string("Compatibility alias for runtimeType.", nullable: true),
            "memberVisibility": string("Effective member visibility."),
            "resolutionError": string("Safe error summary when resolution failed.", nullable: true),
        ]),
        "required": .array([
            .string("id"),
            .string("metadata"),
            .string("hostRuntime"),
            .string("referenceType"),
            .string("available"),
            .string("memberVisibility"),
        ]),
    ])

    private static func object(
        description: String,
        properties: [String: JSONValue] = [:],
        required: [String] = []
    ) -> AnsightToolSchema {
        var result: [String: JSONValue] = [
            "type": .string("object"),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]

        if !properties.isEmpty {
            result["properties"] = .object(properties)
        }

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
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        primitive(type: "string", description: description, nullable: nullable, format: format)
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
        nullable: Bool = false,
        format: String? = nil
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string(type), .string("null")]) : .string(type),
            "additionalProperties": .bool(false),
            "description": .string(description),
        ]

        if let format {
            result["format"] = .string(format)
        }

        return .object(result)
    }
}
