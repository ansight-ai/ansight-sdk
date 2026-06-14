import AnsightKit
import Foundation

internal enum AnsightDatabaseToolSchemas {
    static let listDatabasesArguments = object(
        description: "Arguments for discovering SQLite databases in the app sandbox.",
        properties: [
            "includeSystemStores": boolean("Include cache/system store databases."),
            "maxResults": integer("Maximum number of database entries to return."),
        ]
    )

    static let listDatabasesResult = object(
        description: "Database discovery payload.",
        properties: [
            "databases": array(databaseEntry, "Discovered databases."),
            "truncated": boolean("Whether additional results were omitted."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["databases", "truncated", "capturedAtUtc"]
    )

    static let describeSchemaArguments = object(
        description: "Arguments for describing a database schema.",
        properties: [
            "path": string("Absolute or sandbox-relative database path."),
            "database": string("Alternate field for the database path.", nullable: true),
            "table": string("Optional table name filter.", nullable: true),
        ],
        required: ["path"]
    )

    static let describeSchemaResult = object(
        description: "Database schema description payload.",
        properties: [
            "databasePath": string("Resolved database path."),
            "tables": array(table, "Table and view definitions."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["databasePath", "tables", "capturedAtUtc"]
    )

    static let queryArguments = object(
        description: "Arguments for executing a read-only SQL query.",
        properties: [
            "path": string("Absolute or sandbox-relative database path."),
            "database": string("Alternate field for the database path.", nullable: true),
            "sql": string("Read-only SQL statement."),
            "maxRows": integer("Maximum number of rows to return."),
        ],
        required: ["path", "sql"]
    )

    static let queryResult = object(
        description: "Read-only SQL query result payload.",
        properties: [
            "databasePath": string("Resolved database path."),
            "sql": string("Executed SQL."),
            "columns": array(string("Column name."), "Column names in result order."),
            "columnMetadata": array(columnMetadata, "Column metadata in result order."),
            "rows": array(genericObject, "Row values keyed by stable columnMetadata.key values."),
            "rowValues": array(array(rowCell, "Ordered row cells."), "Rows represented as ordered cells with runtime storage type metadata."),
            "truncated": boolean("Whether additional rows were omitted."),
            "capturedAtUtc": string("UTC timestamp for capture.", format: "date-time"),
        ],
        required: ["databasePath", "sql", "columns", "columnMetadata", "rows", "rowValues", "truncated", "capturedAtUtc"]
    )

    private static let genericObject = objectJSON(
        description: "Arbitrary object with implementation-specific fields.",
        properties: [:],
        additionalProperties: true
    )

    private static let databaseEntry = objectJSON(
        description: "Discovered SQLite database entry.",
        properties: [
            "name": string("Database file name."),
            "path": string("Absolute database path."),
            "relativePath": string("Path relative to the sandbox root."),
            "rootAlias": string("Sandbox root alias."),
            "sizeBytes": integer("Database file size."),
            "lastModifiedUtc": string("Last modification time.", format: "date-time"),
        ],
        required: ["name", "path", "relativePath", "rootAlias", "sizeBytes", "lastModifiedUtc"]
    )

    private static let table = objectJSON(
        description: "Database table or view metadata.",
        properties: [
            "name": string("Table or view name."),
            "type": string("SQLite object type.", nullable: true),
            "sql": string("Create statement.", nullable: true),
            "columns": array(genericObject, "SQLite pragma column metadata."),
            "indexes": array(genericObject, "SQLite pragma index metadata."),
        ],
        required: ["name", "columns", "indexes"]
    )

    private static let columnMetadata = objectJSON(
        description: "Query result column metadata.",
        properties: [
            "index": integer("Zero-based result column index."),
            "name": string("SQLite result column name."),
            "key": string("Stable unique key used for row objects."),
            "declaredType": string("Declared SQLite column type when available.", nullable: true),
            "sourceDatabase": string("Source database name when SQLite exposes it.", nullable: true),
            "sourceTable": string("Source table name when SQLite exposes it.", nullable: true),
            "sourceColumn": string("Source column name when SQLite exposes it.", nullable: true),
        ],
        required: ["index", "name", "key"]
    )

    private static let rowCell = objectJSON(
        description: "Ordered query cell with runtime SQLite storage type and value.",
        properties: [
            "columnKey": string("Stable column key matching columnMetadata.key."),
            "columnName": string("SQLite result column name."),
            "storageType": string("Runtime SQLite storage type.", enumValues: ["integer", "real", "text", "blob", "null", "unknown"]),
            "value": genericObjectOrPrimitive,
        ],
        required: ["columnKey", "columnName", "storageType", "value"],
        additionalProperties: true
    )

    private static let genericObjectOrPrimitive: JSONValue = .object([
        "description": .string("Cell value."),
    ])

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
        required: [String] = [],
        nullable: Bool = false,
        additionalProperties: Bool = false
    ) -> JSONValue {
        var result: [String: JSONValue] = [
            "type": nullable ? .array([.string("object"), .string("null")]) : .string("object"),
            "additionalProperties": .bool(additionalProperties),
            "description": .string(description),
            "properties": .object(properties),
        ]

        if !required.isEmpty {
            result["required"] = .array(required.map(JSONValue.string))
        }

        return .object(result)
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
