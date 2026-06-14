import AnsightKit
import Foundation

internal struct AnsightSQLiteColumnMetadata: Sendable, Equatable {
    let index: Int
    let name: String
    let key: String
    let declaredType: String?
    let sourceDatabase: String?
    let sourceTable: String?
    let sourceColumn: String?

    var jsonValue: JSONValue {
        .object([
            "index": .integer(Int64(index)),
            "name": .string(name),
            "key": .string(key),
            "declaredType": declaredType.map(JSONValue.string) ?? .null,
            "sourceDatabase": sourceDatabase.map(JSONValue.string) ?? .null,
            "sourceTable": sourceTable.map(JSONValue.string) ?? .null,
            "sourceColumn": sourceColumn.map(JSONValue.string) ?? .null,
        ])
    }
}
